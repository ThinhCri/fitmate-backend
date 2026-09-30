using Fitmate.Domain.Common;

namespace Fitmate.Domain.Payment;

public class Refund : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public long AmountVnd { get; private set; }
    public string Reason { get; private set; } = null!;
    public RefundStatus Status { get; private set; }
    public Guid RequestedByAdminId { get; private set; }
    public string? ProviderRefundRef { get; private set; }
    public DateTime? SucceededAtUtc { get; private set; }

    private Refund() { }

    public static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static Refund Create(Guid paymentId, Guid appointmentId, long amountVnd, string reason, Guid adminId) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = paymentId,
        AppointmentId = appointmentId,
        AmountVnd = amountVnd,
        Reason = reason,
        Status = RefundStatus.Pending,
        RequestedByAdminId = adminId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public static Refund CreateSystemRefund(Guid paymentId, Guid appointmentId, long amountVnd, string reason)
        => Create(paymentId, appointmentId, amountVnd, reason, SystemUserId);

    public void MarkSucceeded(string? providerRefundRef, DateTime? succeededAtUtc = null)
    {
        Status = RefundStatus.Succeeded;
        ProviderRefundRef = providerRefundRef;
        SucceededAtUtc = succeededAtUtc ?? DateTime.UtcNow;
        Touch();
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
        Touch();
    }
}
