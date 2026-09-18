using Fitmate.Domain.Common;

namespace Fitmate.Domain.Moderation;

public class Report : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid ReporterId { get; private set; }
    public ReportTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public string Reason { get; private set; } = null!;
    public string? Details { get; private set; }
    public ReportStatus Status { get; private set; }
    public string? AdminNote { get; private set; }
    public Guid? ResolvedByAdminId { get; private set; }

    private Report() { }
    public static Report Create(Guid reporterId, ReportTargetType type, Guid targetId, string reason, string? details) => new()
    {
        Id = Guid.NewGuid(),
        ReporterId = reporterId,
        TargetType = type,
        TargetId = targetId,
        Reason = reason,
        Details = details,
        Status = ReportStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Resolve(Guid adminId, string action, string? note)
    {
        Status = ReportStatus.Resolved;
        ResolvedByAdminId = adminId;
        AdminNote = $"{action}: {note}".Trim();
        Touch();
    }

    public void Dismiss(Guid adminId, string? note)
    {
        Status = ReportStatus.Dismissed;
        ResolvedByAdminId = adminId;
        AdminNote = note;
        Touch();
    }
}
