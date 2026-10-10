using Fitmate.Application.Booking;
using System.Text.Json.Serialization;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Payments;

public sealed record PackageCheckoutView(
    [property: JsonPropertyName("package_id")] Guid PackageId,
    [property: JsonPropertyName("payment_id")] Guid PaymentId,
    [property: JsonPropertyName("amount_vnd")] long AmountVnd,
    [property: JsonPropertyName("status")] PaymentStatus Status,
    [property: JsonPropertyName("redirect_url")] string? RedirectUrl);

/// <summary>
/// One-order checkout preparation for a package. All financial consumers
/// verify the persisted per-session allocation before refund or payout.
/// </summary>
public sealed class PackagePaymentCheckoutService(
    IPackagePaymentStore packages,
    IPaymentStore payments,
    IAppointmentStore appointments,
    IPaymentProvider provider)
{
    public async Task<PackageCheckoutView> CreateAsync(Guid memberId, Guid packageId,
        CancellationToken ct, bool acknowledgeShortNotice = false)
    {
        if (memberId == Guid.Empty || packageId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        if (provider is not IPaymentReservation reservation || provider.Method != PaymentMethod.Payos)
            throw new DomainException(ErrorCodes.Payment.ProviderUnavailable,
                "Package checkout requires configured payOS.", 503);

        var candidate = await packages.FindWithSessionsAsync(packageId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Package not found.");
        if (candidate.MemberId != memberId)
            throw new NotFoundException(ErrorCodes.General.NotFound, "Package not found.");
        var payment = await appointments.ExecuteWithScheduleLockAsync(candidate.PtUserId, async () =>
        {
            var package = await packages.FindWithSessionsAsync(packageId, ct)
                ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Package not found.");
            if (package.MemberId != memberId)
                throw new NotFoundException(ErrorCodes.General.NotFound, "Package not found.");
            var nowUtc = DateTime.UtcNow;
            if (package.HoldExpiresAtUtc <= nowUtc ||
                !await packages.AllReservationsActiveAsync(package, nowUtc, ct))
                throw new ConflictException("PACKAGE_HOLD_EXPIRED",
                    "One or more package reservations are no longer available.");
            var existing = await packages.FindPaymentAsync(packageId, ct);
            if (existing is not null)
            {
                if (package.Status != SessionPackageStatus.PaymentPending ||
                    existing.Status != PaymentStatus.Pending)
                    throw new ConflictException("PACKAGE_PAYMENT_ALREADY_PROCESSED",
                        "The package payment is no longer pending.");
                return existing;
            }

            if (package.Status != SessionPackageStatus.Held)
                throw new ConflictException("PACKAGE_PAYMENT_ALREADY_PROCESSED",
                    "The package is no longer awaiting payment.");

            if (!acknowledgeShortNotice && package.Sessions.Any(s =>
                s.InitialSlotStartUtc < nowUtc.AddHours(48)))
                throw new DomainException("PACKAGE_SHORT_NOTICE_ACK_REQUIRED",
                    "A session starts within 48 hours. Acknowledge the cancellation policy before payment.", 400);

            var pending = Payment.CreatePendingForPackage(package.Id, PaymentMethod.Payos,
                package.TotalPriceVnd);
            payments.Add(pending);
            package.AttachPayment(pending.Id, nowUtc);
            reservation.Reserve(pending.Id, package.HoldExpiresAtUtc);
            return pending;
        }, ct);

        if (payment.RedirectUrl is null)
        {
            // External HTTP is deliberately outside the PT schedule transaction.
            var result = await provider.CreatePaymentAsync(
                new ProviderCreatePaymentRequest(payment.Id, payment.AmountVnd, "FITMATE"), ct);
            payment.SetCheckoutUrl(result.RedirectUrl);
            await payments.SaveChangesAsync(ct);
        }

        return new PackageCheckoutView(packageId, payment.Id, payment.AmountVnd,
            payment.Status, payment.RedirectUrl);
    }
}
