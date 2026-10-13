using System.Text.Json;
using Fitmate.Application.Booking;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
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
            Text(data, "currency") != "VND" || Text(data, "accountNumber") != config.AccountNumber ||
            string.IsNullOrWhiteSpace(Text(data, "reference")) || Text(data, "reference")!.Length > 128)
            throw new DomainException("PAYMENT_DATA_INVALID", "Invalid payment identity, account, currency or amount.", 400);

        var linkId = Text(data, "paymentLinkId");
        if (!Guid.TryParseExact(linkId, "N", out _) || (order.PaymentLinkId != null && linkId != order.PaymentLinkId))
            throw new ConflictException("PAYMENT_LINK_MISMATCH", "Payment link does not match the order.");

        var packageId = await db.Payments.AsNoTracking().Where(p => p.Id == order.PaymentId)
            .Select(p => p.PackageId).SingleOrDefaultAsync(ct);
        if (packageId.HasValue)
        {
            var ptUserId = await db.SessionPackages.AsNoTracking().Where(p => p.Id == packageId.Value)
                .Select(p => p.PtUserId).SingleOrDefaultAsync(ct);
            if (ptUserId == Guid.Empty)
                throw new NotFoundException("PACKAGE_NOT_FOUND", "Package not found.");
            await appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
            {
                var payment = await store.FindByIdForUpdateAsync(order.PaymentId, ct)
                    ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
                await SettlePackageAsync(payment, packageId.Value, order, linkId!, amountVnd,
                    Text(data, "reference"), ct);
                return true;
            }, ct);
            return;
        }

        await store.ExecuteInTransactionAsync(async () =>
        {
            var payment = await store.FindByIdForUpdateAsync(order.PaymentId, ct)
                ?? throw new NotFoundException("PAYMENT_NOT_FOUND", "Payment not found.");
            if (payment.Method != PaymentMethod.Payos || payment.AmountVnd != amountVnd)
                throw new ConflictException("PAYMENT_AMOUNT_MISMATCH", "Payment amount or method does not match.");
            if (payment.Status is PaymentStatus.Paid or PaymentStatus.PartialRefunded or PaymentStatus.Refunded) return;
            if (payment.Status != PaymentStatus.Pending)
                throw new ConflictException("PAYMENT_RECONCILIATION_REQUIRED", "Payment requires manual reconciliation.");
            var appointmentId = payment.RequireAppointmentId();
            var appointment = await store.FindAppointmentForUpdateAsync(appointmentId, ct);
            var hold = appointment == null ? null : await store.FindHoldForUpdateAsync(appointment.Id, ct);
            payment.MarkPaid(Text(data, "reference"));
            order.BindLink(linkId!);
            if (appointment != null && appointment.Status == AppointmentStatus.Held && hold is { IsActive: true })
            {
                var fee = (long)Math.Round(amountVnd * 0.10m, MidpointRounding.AwayFromZero);
                appointment.AttachPayment(payment.Id, amountVnd, 0, 0.10m, fee, amountVnd - fee, "v1");
                appointment.ConfirmByPt();
                hold.Consume();
                outbox.Enqueue("payment.paid", new { paymentId = payment.Id, appointmentId = appointment.Id, amountVnd });
                outbox.Enqueue("booking.confirmed", new { appointmentId = appointment.Id, ptUserId = appointment.PtUserId, memberId = appointment.MemberId, confirmationSource = "verified_payment" });
            }
            else
            {
                var refund = Refund.CreateSystemRefund(payment.Id, appointmentId, amountVnd, "Payment received after booking hold expired.");
                store.AddRefund(refund);
                outbox.Enqueue("payment.settlement_discrepancy", new { paymentId = payment.Id, appointmentId = payment.AppointmentId, refundId = refund.Id, amountVnd });
            }
            await store.SaveChangesAsync(ct);
        }, ct);
    }

    private async Task SettlePackageAsync(Payment payment, Guid packageId, PayosOrder order,
        string linkId, long amountVnd, string? reference, CancellationToken ct)
    {
        if (payment.PackageId != packageId || payment.Method != PaymentMethod.Payos ||
            payment.AmountVnd != amountVnd)
            throw new ConflictException("PAYMENT_AMOUNT_MISMATCH", "Package payment identity, amount or method does not match.");
        if (payment.Status is PaymentStatus.Paid or PaymentStatus.PartialRefunded or PaymentStatus.Refunded) return;
        if (payment.Status != PaymentStatus.Pending)
            throw new ConflictException("PAYMENT_RECONCILIATION_REQUIRED", "Payment requires manual reconciliation.");

        var package = await db.SessionPackages.Include(p => p.Sessions)
            .SingleOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new NotFoundException("PACKAGE_NOT_FOUND", "Package not found.");
        if (package.Status != SessionPackageStatus.PaymentPending || package.PaymentId != payment.Id ||
            package.TotalPriceVnd != amountVnd || package.Sessions.Count != package.SessionCount ||
            package.Sessions.Sum(s => s.PaidAllocationVnd) != amountVnd ||
            package.Sessions.Any(s => s.PaidAllocationVnd != s.PlatformFeeAllocationVnd + s.TrainerShareAllocationVnd))
            throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Package allocation requires manual reconciliation.");

        var appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray();
        var holdIds = package.Sessions.Select(s => s.HoldId).ToArray();
        var bookings = await db.Appointments.Where(a => appointmentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var holds = await db.BookingHolds.Where(h => holdIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, ct);
        if (bookings.Count != package.SessionCount || holds.Count != package.SessionCount)
            throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "Package reservations are incomplete.");

        var nowUtc = DateTime.UtcNow;
        var active = nowUtc < package.HoldExpiresAtUtc && package.Sessions.All(s =>
            bookings[s.AppointmentId].Status == AppointmentStatus.Held &&
            bookings[s.AppointmentId].PaymentId == null &&
            bookings[s.AppointmentId].MemberId == package.MemberId &&
            bookings[s.AppointmentId].PtUserId == package.PtUserId &&
            holds[s.HoldId].Status == "active" && nowUtc < holds[s.HoldId].ExpiresAt &&
            holds[s.HoldId].MemberId == package.MemberId &&
            holds[s.HoldId].PtUserId == package.PtUserId &&
            holds[s.HoldId].SlotStartUtc == bookings[s.AppointmentId].SlotStartUtc);

        payment.MarkPaid(reference);
        order.BindLink(linkId);
        if (active)
        {
            package.MarkPaid(payment.Id, amountVnd, nowUtc);
            foreach (var session in package.Sessions)
            {
                var booking = bookings[session.AppointmentId];
                booking.AttachPayment(payment.Id, session.PaidAllocationVnd, 0, .10m,
                    session.PlatformFeeAllocationVnd, session.TrainerShareAllocationVnd, "package-v1", nowUtc);
                booking.ConfirmByPt();
                holds[session.HoldId].Consume();
                outbox.Enqueue("booking.confirmed", new
                {
                    appointmentId = booking.Id, ptUserId = package.PtUserId,
                    memberId = package.MemberId, confirmationSource = "verified_package_payment"
                });
            }
            outbox.Enqueue("payment.paid", new { paymentId = payment.Id, packageId, amountVnd });
        }
        else
        {
            if (package.Sessions.Any(s => bookings[s.AppointmentId].Status != AppointmentStatus.Held ||
                                          bookings[s.AppointmentId].PaymentId != null))
                throw new ConflictException("PACKAGE_RECONCILIATION_REQUIRED", "A package session changed before payment settlement.");
            package.MarkLatePaymentForRefund(payment.Id, nowUtc);
            foreach (var session in package.Sessions)
            {
                bookings[session.AppointmentId].ExpireHold();
                holds[session.HoldId].Release();
            }
            var anchorId = package.Sessions.OrderBy(s => s.Sequence).First().AppointmentId;
            var refund = Refund.CreateSystemRefund(payment.Id, anchorId, amountVnd,
                "Payment received after package reservations expired.");
            store.AddRefund(refund);
            outbox.Enqueue("payment.settlement_discrepancy", new
            {
                paymentId = payment.Id, packageId, refundId = refund.Id, amountVnd
            });
        }
        await store.SaveChangesAsync(ct);
    }

    private static string? Text(JsonElement data, string key) =>
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
