using Fitmate.Domain.Common;

namespace Fitmate.Domain.Payments;

public class Payment : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid HoldId { get; private set; }
    public PaymentMethod Method { get; private set; }
    public PaymentStatus Status { get; private set; }
    public long AmountVnd { get; private set; }
    public long DiscountVnd { get; private set; }
    public string? RedirectUrl { get; private set; }
    public string? ProviderTxnRef { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid memberId, Guid appointmentId, Guid holdId,
        PaymentMethod method, long amountVnd, long discountVnd) => new()
    {
        Id = Guid.NewGuid(),
        MemberId = memberId,
        AppointmentId = appointmentId,
        HoldId = holdId,
        Method = method,
        Status = PaymentStatus.Pending,
        AmountVnd = amountVnd,
        DiscountVnd = discountVnd,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void AttachProvider(string providerTxnRef, string redirectUrl)
    {
        ProviderTxnRef = providerTxnRef;
        RedirectUrl = redirectUrl;
        Touch();
    }

    public void MarkPaid()
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Cannot mark payment as Paid from status {Status}.");
        Status = PaymentStatus.Paid;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void MarkFailed()
    {
        if (Status == PaymentStatus.Paid || Status == PaymentStatus.Refunded)
            return;
        Status = PaymentStatus.Failed;
        Touch();
    }

    public void MarkRefunded() => MarkRefunded(false);

    public void MarkRefunded(bool partial)
    {
        if (partial)
        {
            Status = PaymentStatus.PartialRefunded;
        }
        else
        {
            if (Status != PaymentStatus.Paid)
                throw new InvalidOperationException($"Cannot refund payment in status {Status}.");
            Status = PaymentStatus.Refunded;
        }
        Touch();
    }
}
