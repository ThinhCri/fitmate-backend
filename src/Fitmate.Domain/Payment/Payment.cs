using Fitmate.Domain.Common;

namespace Fitmate.Domain.Payment;

public class Payment : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? PackageId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public long AmountVnd { get; private set; }
    public string? RedirectUrl { get; private set; }
    public string? ProviderTxnRef { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public long RefundedSoFarVnd { get; private set; }

    private Payment() { }

    public static Payment CreatePending(Guid appointmentId, PaymentMethod method, long amountVnd, string? redirectUrl) => new()
    {
        Id = Guid.NewGuid(),
        AppointmentId = appointmentId,
        Method = method,
        Status = PaymentStatus.Pending,
        AmountVnd = amountVnd,
        RedirectUrl = redirectUrl,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public static Payment CreatePendingForPackage(Guid packageId, PaymentMethod method, long amountVnd) =>
        packageId == Guid.Empty || amountVnd <= 0 || method != PaymentMethod.Payos
            ? throw new ArgumentException("Package payment requires a package, payOS and positive amount.")
            : new Payment
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                Method = method,
                Status = PaymentStatus.Pending,
                AmountVnd = amountVnd,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

    public Guid RequireAppointmentId() =>
        PackageId is null && AppointmentId.HasValue
            ? AppointmentId.Value
            : throw new ConflictException("PACKAGE_PAYMENT_FLOW_REQUIRED",
                "This payment belongs to a PT package and needs the package finance workflow.");

    public void MarkPaid(string? providerTxnRef)
    {
        if (Status == PaymentStatus.Paid) return;
        if (Status != PaymentStatus.Pending)
            throw new ConflictException(ErrorCodes.Payment.AlreadyProcessed, $"Payment already {Status}.");
        Status = PaymentStatus.Paid;
        ProviderTxnRef = providerTxnRef;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void SetCheckoutUrl(string redirectUrl)
    {
        RedirectUrl = redirectUrl;
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    public void ApplyRefund(long amountVnd)
    {
        if (amountVnd <= 0) throw new ArgumentException("amountVnd > 0");
        var newTotal = RefundedSoFarVnd + amountVnd;
        if (newTotal > AmountVnd)
            throw new ConflictException(ErrorCodes.Payment.RefundCapExceeded,
                $"Total refund {newTotal} exceeds paid {AmountVnd}.");
        RefundedSoFarVnd = newTotal;
        Status = newTotal == AmountVnd ? PaymentStatus.Refunded : PaymentStatus.PartialRefunded;
        Touch();
    }
}
