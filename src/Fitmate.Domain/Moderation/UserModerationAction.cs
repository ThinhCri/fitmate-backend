using Fitmate.Domain.Common;

namespace Fitmate.Domain.Moderation;

/// <summary>
/// Loại action moderation áp dụng lên user khi admin resolve report.
/// Theo kế hoạch BE-211 — Warn (cảnh cáo) và Lock (khóa tài khoản) đều
/// tạo 1 row trong bảng <c>user_moderation_actions</c> để audit + cho user
/// xem lại lịch sử vi phạm.
/// </summary>
public enum UserModerationActionKind
{
    Warn = 0,
    Lock = 1
}

/// <summary>
/// Lịch sử hành động moderation áp lên user. Mỗi lần admin resolve report
/// bằng action Warn / LockPermanent / Lock30d / Lock7d / Lock24h tạo 1 row.
/// <c>ExpiresAt</c> set cho Lock có duration; Warn để null.
/// </summary>
public class UserModerationAction : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public UserModerationActionKind Action { get; private set; }
    public Guid? ReportId { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public Guid ActorAdminId { get; private set; }
    public string? Reason { get; private set; }
    public string? MetadataJson { get; private set; }

    private UserModerationAction() { }

    public static UserModerationAction Record(
        Guid userId,
        UserModerationActionKind action,
        Guid actorAdminId,
        Guid? reportId = null,
        DateTime? expiresAt = null,
        string? reason = null,
        string? metadataJson = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Action = action,
        ReportId = reportId,
        ExpiresAt = expiresAt,
        ActorAdminId = actorAdminId,
        Reason = reason,
        MetadataJson = metadataJson,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
