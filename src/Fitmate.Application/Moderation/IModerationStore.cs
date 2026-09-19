using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

public interface IModerationStore
{
    Task<(IReadOnlyList<ReportRow> Items, long Total)> ListReportsAsync(
        ReportStatus? status, int skip, int take, CancellationToken ct);

    Task<ReportRow?> GetReportAsync(Guid reportId, CancellationToken ct);
    Task SaveReportAsync(ReportRow row, CancellationToken ct);
}

public sealed record ReportRow(
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
