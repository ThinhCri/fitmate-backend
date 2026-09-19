using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

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
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ReportPage(IReadOnlyList<ReportView> Items, long Total);

public interface IModerationService
{
    Task<ReportPage> ListReportsAsync(Guid adminUserId, ReportStatus? status, int page, int pageSize, CancellationToken ct);
    Task<ReportView> ResolveReportAsync(Guid adminUserId, Guid reportId, ReportResolveAction action, string? note, CancellationToken ct);
    Task<ReportView> DismissReportAsync(Guid adminUserId, Guid reportId, string? note, CancellationToken ct);
}
