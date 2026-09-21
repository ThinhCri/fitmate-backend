using Fitmate.Domain.Common;

namespace Fitmate.Domain.Payments;

public class Refund : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public long AmountVnd { get; private set; }
    public string Reason { get; private set; } = null!;
    public RefundStatus Status { get; private set; }

    private Refund() { }

    public static Refund Create(
        Guid paymentId, Guid appointmentId, long amountVnd, string reason) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = paymentId,
        AppointmentId = appointmentId,
        AmountVnd = amountVnd,
        Reason = reason,
        Status = RefundStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void MarkSucceeded()
    {
        Status = RefundStatus.Succeeded;
        Touch();
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
        Touch();
    }
}
