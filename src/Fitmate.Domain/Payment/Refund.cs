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
    public Guid? ApprovedByAdminId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? TransferDueAtUtc => ApprovedAtUtc?.AddHours(48);

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

    public void Approve(Guid adminId, DateTime? approvedAtUtc = null)
    {
        if (Status != RefundStatus.Pending)
            throw new ConflictException("REFUND_ALREADY_PROCESSED", "Only a pending refund can be approved.");
        if (ApprovedAtUtc.HasValue)
            throw new ConflictException("REFUND_ALREADY_APPROVED", "Refund has already been approved.");
        var now = approvedAtUtc ?? DateTime.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Approval time must be UTC.", nameof(approvedAtUtc));
        ApprovedByAdminId = adminId;
        ApprovedAtUtc = now;
        Touch();
    }

    public void MarkSucceeded(string? providerRefundRef, DateTime? succeededAtUtc = null)
    {
        var transferredAt = succeededAtUtc ?? DateTime.UtcNow;
        Status = RefundStatus.Succeeded;
        ProviderRefundRef = providerRefundRef;
        SucceededAtUtc = transferredAt;
        Touch();
    }

    public void MarkManualTransferSucceeded(string bankReference, DateTime transferredAtUtc)
    {
        if (Status != RefundStatus.Pending)
            throw new ConflictException("REFUND_ALREADY_PROCESSED", "Only a pending refund can be transferred.");
        if (!ApprovedAtUtc.HasValue)
            throw new ConflictException("REFUND_NOT_APPROVED", "Refund must be approved before recording the transfer.");
        if (string.IsNullOrWhiteSpace(bankReference))
            throw new ArgumentException("Bank reference is required.", nameof(bankReference));
        if (transferredAtUtc.Kind != DateTimeKind.Utc || transferredAtUtc < ApprovedAtUtc.Value)
            throw new ConflictException("REFUND_TRANSFER_TIME_INVALID", "Transfer time must be UTC and after approval.");
        MarkSucceeded(bankReference, transferredAtUtc);
    }

    public void MarkFailed()
    {
        Status = RefundStatus.Failed;
        Touch();
    }
}
