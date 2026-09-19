using Fitmate.Application.Moderation;
using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Moderation;

/// <summary>
/// EF Core impl cho moderation store.
/// Reports không soft-delete (giữ lại cho audit). Pagination skip/take (page/pageSize).
/// </summary>
public class EfModerationStore : IModerationStore
{
    private readonly FitmateDbContext _db;
    public EfModerationStore(FitmateDbContext db) { _db = db; }

    public async Task<(IReadOnlyList<ReportRow> Items, long Total)> ListReportsAsync(
        ReportStatus? status, int skip, int take, CancellationToken ct)
    {
        var q = _db.Reports.AsQueryable();
        if (status.HasValue) q = q.Where(r => r.Status == status.Value);

        var total = await q.LongCountAsync(ct);

        var entities = await q.OrderByDescending(r => r.CreatedAt)
                              .Skip(skip)
                              .Take(take)
                              .ToListAsync(ct);

        var items = entities.Select(ToRow).ToList();
        return (items, total);
    }

    public async Task<ReportRow?> GetReportAsync(Guid reportId, CancellationToken ct)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(x => x.Id == reportId, ct);
        return r is null ? null : ToRow(r);
    }

    public async Task SaveReportAsync(ReportRow row, CancellationToken ct)
    {
        var entity = await _db.Reports.FirstOrDefaultAsync(r => r.Id == row.Id, ct)
            ?? throw new InvalidOperationException($"Report '{row.Id}' not found.");
        // Apply transition via domain methods (preserve state machine invariants).
        if (row.Status == ReportStatus.Resolved)
            entity.Resolve(row.ResolvedByAdminId ?? Guid.Empty, ExtractAction(row.AdminNote), row.AdminNote);
        else if (row.Status == ReportStatus.Dismissed)
            entity.Dismiss(row.ResolvedByAdminId ?? Guid.Empty, row.AdminNote);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Extract leading "Action:" token from composed AdminNote (best-effort; falls back to original).</summary>
    private static string ExtractAction(string? composed)
    {
        if (string.IsNullOrEmpty(composed)) return string.Empty;
        var i = composed.IndexOf(':');
        return i > 0 ? composed[..i] : composed;
    }

    private static ReportRow ToRow(Report r) => new(
        Id: r.Id, ReporterId: r.ReporterId, TargetType: r.TargetType, TargetId: r.TargetId,
        Reason: r.Reason, Details: r.Details, Status: r.Status,
        AdminNote: r.AdminNote, ResolvedByAdminId: r.ResolvedByAdminId,
        CreatedAt: r.CreatedAt, UpdatedAt: r.UpdatedAt);
}
