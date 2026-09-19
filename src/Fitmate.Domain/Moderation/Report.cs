using Fitmate.Domain.Common;

namespace Fitmate.Domain.Moderation;

/// <summary>
/// Loại resolve action mà admin chọn (lưu trên <see cref="Report.ResolveAction"/>
/// để audit + cho user xem lại). Theo kế hoạch BE-211 — giá trị <see cref="ResolveActionValue"/>
/// lưu nguyên văn từ client (warn / lock_permanent / lock_30d / lock_7d / lock_24h / delete_content / no_action)
/// để audit có đủ chi tiết (duration, có xóa content hay không).
/// </summary>
public static class ResolveActionValue
{
    public const string Warn = "warn";
    public const string LockPermanent = "lock_permanent";
    public const string Lock30d = "lock_30d";
    public const string Lock7d = "lock_7d";
    public const string Lock24h = "lock_24h";
    public const string DeleteContent = "delete_content";
    public const string NoAction = "no_action";

    /// <summary>Tất cả value lock hợp lệ (để resolve lock_duration).</summary>
    public static readonly IReadOnlyCollection<string> LockValues = new[]
    {
        LockPermanent, Lock30d, Lock7d, Lock24h
    };

    /// <summary>Tất cả value hợp lệ mà admin có thể chọn.</summary>
    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Warn, LockPermanent, Lock30d, Lock7d, Lock24h, DeleteContent, NoAction
    };
}

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
    /// <summary>BE-211: idempotency key từ header <c>Idempotency-Key</c>; null nếu client không gửi.</summary>
    public string? IdempotencyKey { get; private set; }
    /// <summary>BE-211: action subtype mà admin chọn (vd <c>lock_30d</c>). Lưu raw để audit.</summary>
    public string? ResolveAction { get; private set; }

    private Report() { }
    public static Report Create(
        Guid reporterId,
        ReportTargetType type,
        Guid targetId,
        string reason,
        string? details,
        string? idempotencyKey = null) => new()
    {
        Id = Guid.NewGuid(),
        ReporterId = reporterId,
        TargetType = type,
        TargetId = targetId,
        Reason = reason,
        Details = details,
        Status = ReportStatus.Pending,
        IdempotencyKey = idempotencyKey,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Resolve(Guid adminId, string resolveAction, string? note)
    {
        if (Status != ReportStatus.Pending)
            throw new ConflictException(ErrorCodes.Moderation.ReportNotPending, $"Report '{Id}' already {Status}.");
        Status = ReportStatus.Resolved;
        ResolvedByAdminId = adminId;
        ResolveAction = resolveAction;
        AdminNote = note;
        Touch();
    }

    public void Dismiss(Guid adminId, string? note)
    {
        if (Status != ReportStatus.Pending)
            throw new ConflictException(ErrorCodes.Moderation.ReportNotPending, $"Report '{Id}' already {Status}.");
        Status = ReportStatus.Dismissed;
        ResolvedByAdminId = adminId;
        AdminNote = note;
        Touch();
    }
}
