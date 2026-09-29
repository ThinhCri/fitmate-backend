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
    /// <summary>
    /// Upload identity for <see cref="ImageStorageKey"/>. Null when no image has been
    /// uploaded yet OR for legacy rows predating the ownership migration.
    /// Admin preview verifies the recorded uploader matches <see cref="PtUserId"/>;
    /// a null value is rejected (404) rather than attributed automatically.
    /// </summary>
    public Guid? ImageUploadedByUserId { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public Guid? VerifiedByAdminId { get; private set; }

    private Certificate() { }

    public static Certificate Submit(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey, Guid? imageUploadedByUserId = null) => new()
    {
        Id = Guid.NewGuid(),
        PtUserId = ptUserId,
        Title = title,
        IssuedBy = issuedBy,
        Code = code,
        IssueDate = issueDate,
        Status = CertificateStatus.Pending,
        ImageStorageKey = imageStorageKey,
        ImageUploadedByUserId = imageUploadedByUserId,
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

    public void Revoke(Guid adminId, string reason)
    {
        if (Status != CertificateStatus.Approved)
            throw new ConflictException(ErrorCodes.Trainer.CertificateNotPending, "Only approved certificates can be revoked.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Length > 500)
            throw new ValidationException(ErrorCodes.Trainer.CertificateMissingReason, "A reason of 5 to 500 characters is required.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "5 to 500 characters" } });
        Status = CertificateStatus.Revoked;
        RejectReason = reason.Trim();
        VerifiedByAdminId = adminId;
        VerifiedAt = DateTime.UtcNow;
        Touch();
    }
}
