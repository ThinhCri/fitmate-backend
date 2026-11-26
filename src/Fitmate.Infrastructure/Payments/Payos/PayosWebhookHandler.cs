using System.Text.Json;
using Fitmate.Application.Booking;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Payments.Payos;

public sealed class PayosWebhookHandler(FitmateDbContext db, IPaymentStore store, IAppointmentStore appointments,
    IOutboxDispatcher outbox, IOptions<PayosOptions> options) : IPayosWebhookHandler
{
    public async Task HandleAsync(JsonElement body, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.CanVerifyWebhook())
            throw new DomainException("PAYMENT_PROVIDER_UNAVAILABLE", "payOS webhook verification is not configured.", 503);
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
        // Registration may precede merchant approval; never settle a real order while checkout is disabled.
        if (!config.Enabled || !config.IsValid())
            throw new DomainException("PAYMENT_PROVIDER_UNAVAILABLE", "payOS checkout is disabled.", 503);
        if (!data.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.Number || !amount.TryGetInt64(out var amountVnd) || amountVnd <= 0 ||
            Text(data, "currency") != "VND" || !PayosAccountNumber.Matches(Text(data, "accountNumber"), config.AccountNumber) ||
            string.IsNullOrWhiteSpace(Text(data, "reference")) || Text(data, "reference")!.Length > 128)
            throw new DomainException("PAYMENT_DATA_INVALID", "Invalid payment identity, account, currency or amount.", 400);

        var linkId = Text(data, "paymentLinkId")?.Replace("-", "");
        if (!Guid.TryParseExact(linkId, "N", out _) || (order.PaymentLinkId != null && linkId != order.PaymentLinkId.Replace("-", "")))
            throw new ConflictException("PAYMENT_LINK_MISMATCH", "Payment link does not match the order.");

        var executor = new PayosSettlementExecutor(db, store, appointments, outbox);
        await executor.SettleVerifiedOrderAsync(order, linkId!, amountVnd, Text(data, "reference")!, ct);
    }

    private static string? Text(JsonElement data, string key) =>
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
