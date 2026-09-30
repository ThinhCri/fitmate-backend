using System.Text.Json;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Payments.Payos;

public sealed class PayosWebhookHandler(FitmateDbContext db, IPaymentStore store,
    IOutboxDispatcher outbox, IOptions<PayosOptions> options) : IPayosWebhookHandler
{
    public async Task HandleAsync(JsonElement body, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled || !config.IsValid())
            throw new DomainException("PAYMENT_PROVIDER_UNAVAILABLE", "payOS webhook is disabled.", 503);
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var data) ||
            !body.TryGetProperty("signature", out var sig) || sig.ValueKind != JsonValueKind.String ||
            !PayosSignature.Verify(data, sig.GetString(), config.ChecksumKey))
            throw new DomainException("PAYMENT_SIGNATURE_INVALID", "Invalid payment signature.", 400);
        // Only the SIGNED inner data can authorize settlement, never the outer success flag.
        if (Text(data, "code") != "00") return;
        if (!data.TryGetProperty("orderCode", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt64(out var orderCode))
            throw new DomainException("PAYMENT_DATA_INVALID", "Invalid order code.", 400);
        var order = await db.Set<PayosOrder>().SingleOrDefaultAsync(o => o.OrderCode == orderCode, ct);
        // Signed registration probes and unknown orders are acknowledged without a mutation.
        if (order == null) return;
        if (!data.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt64(out var amountVnd) || amountVnd <= 0 ||
            Text(data, "currency") != "VND" || Text(data, "accountNumber") != config.AccountNumber ||
            string.IsNullOrWhiteSpace(Text(data, "reference")) || Text(data, "reference")!.Length > 128)
            throw new DomainException("PAYMENT_DATA_INVALID", "Invalid payment identity, account, currency or amount.", 400);

        var linkId = Text(data, "paymentLinkId");
        if (!Guid.TryParseExact(linkId, "N", out _) || (order.PaymentLinkId != null && linkId != order.PaymentLinkId))
            throw new ConflictException("PAYMENT_LINK_MISMATCH", "Payment link does not match the order.");

        await store.ExecuteInTransactionAsync(async () =>
        {
            var payment = await store.FindByIdForUpdateAsync(order.PaymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            if (payment.Method != PaymentMethod.Payos || payment.AmountVnd != amountVnd)
                throw new ConflictException("PAYMENT_AMOUNT_MISMATCH", "Payment amount or method does not match.");
            if (payment.Status is PaymentStatus.Paid or PaymentStatus.PartialRefunded or PaymentStatus.Refunded) return;
            if (payment.Status != PaymentStatus.Pending)
                throw new ConflictException("PAYMENT_RECONCILIATION_REQUIRED", "Payment requires manual reconciliation.");
            var appointment = await store.FindAppointmentForUpdateAsync(payment.AppointmentId, ct);
            var hold = appointment == null ? null : await store.FindHoldForUpdateAsync(appointment.Id, ct);
            payment.MarkPaid(Text(data, "reference"));
            order.BindLink(linkId!);
            if (appointment != null && appointment.Status == AppointmentStatus.Held && hold is { IsActive: true })
            {
                var fee = (long)Math.Round(amountVnd * 0.10m, MidpointRounding.AwayFromZero);
                appointment.AttachPayment(payment.Id, amountVnd, 0, 0.10m, fee, amountVnd - fee, "v1");
                hold.Consume();
                outbox.Enqueue("payment.paid", new { paymentId = payment.Id, appointmentId = appointment.Id, amountVnd });
            }
            else
            {
                var refund = Refund.CreateSystemRefund(payment.Id, payment.AppointmentId, amountVnd, "Payment received after booking hold expired.");
                store.AddRefund(refund);
                outbox.Enqueue("payment.settlement_discrepancy", new { paymentId = payment.Id, appointmentId = payment.AppointmentId, refundId = refund.Id, amountVnd });
            }
            await store.SaveChangesAsync(ct);
        }, ct);
    }

    private static string? Text(JsonElement data, string key) =>
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
