using Fitmate.Domain.Common;

namespace Fitmate.Domain.Trainers;

public class Certificate : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PtUserId { get; private set; }
    public string Title { get; private set; } = null!;
    public string IssuedBy { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public DateOnly IssueDate { get; private set; }
    public CertificateStatus Status { get; private set; }
    public string? ImageStorageKey { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public Guid? VerifiedByAdminId { get; private set; }

    private Certificate() { }

    public static Certificate Submit(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey) => new()
    {
        Id = Guid.NewGuid(),
        PtUserId = ptUserId,
        Title = title,
        IssuedBy = issuedBy,
        Code = code,
        IssueDate = issueDate,
        Status = CertificateStatus.Pending,
        ImageStorageKey = imageStorageKey,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Approve(Guid adminId)
    {
        if (Status != CertificateStatus.Pending)
            throw new ConflictException(ErrorCodes.Trainer.CertificateNotPending, $"Certificate already {Status}.");
        Status = CertificateStatus.Approved;
        VerifiedAt = DateTime.UtcNow;
        VerifiedByAdminId = adminId;
        Touch();
    }

    public void Reject(Guid adminId, string reason)
    {
        if (Status != CertificateStatus.Pending)
            throw new ConflictException(ErrorCodes.Trainer.CertificateNotPending, $"Certificate already {Status}.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 5)
            throw new ValidationException(ErrorCodes.Trainer.CertificateMissingReason, "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });
        Status = CertificateStatus.Rejected;
        RejectReason = reason;
        VerifiedAt = DateTime.UtcNow;
        VerifiedByAdminId = adminId;
        Touch();
    }
}
