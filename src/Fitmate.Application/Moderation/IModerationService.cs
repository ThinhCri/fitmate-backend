using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

/// <summary>
/// BE-211: lock duration option. Map từ <c>ResolveActionValue</c>.
/// </summary>
public enum LockDuration
{
    Permanent = 0,
    Days30 = 1,
    Days7 = 2,
    Hours24 = 3
}

public enum ReportResolveAction
{
    Warn,
    LockTarget,
    DeleteContent,
    NoAction
}

public sealed record ReportView(
    Guid Id,
    Guid ReporterId,
    ReportTargetType TargetType,
    Guid TargetId,
    string Reason,
    string? Details,
    ReportStatus Status,
    string? AdminNote,
    Guid? ResolvedByAdminId,
    string? ResolveAction,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ReportPage(IReadOnlyList<ReportView> Items, long Total);

public interface IModerationService
{
    /// <summary>BE-211: list reports (admin) — giữ nguyên từ BE-210.</summary>
    Task<ReportPage> ListReportsAsync(Guid adminUserId, ReportStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// BE-211: resolve report với action chi tiết + lock_duration (nếu LockTarget).
    /// Actions:
    /// - Warn: tạo UserModerationAction + enqueue outbox user.warning.
    /// - LockTarget: set lockout (Permanent/30d/7d/24h) + bump security stamp + UserModerationAction + audit.
    /// - DeleteContent: soft-delete Post/Comment nếu target phù hợp; reject nếu không.
    /// - NoAction: chỉ đánh dấu resolved + audit.
    /// </summary>
    Task<ReportView> ResolveReportAsync(Guid adminUserId, Guid reportId, ReportResolveAction action,
        LockDuration? lockDuration, string? note, CancellationToken ct);

    /// <summary>BE-211: dismiss report (admin) — giữ nguyên từ BE-210.</summary>
    Task<ReportView> DismissReportAsync(Guid adminUserId, Guid reportId, string? note, CancellationToken ct);
}
