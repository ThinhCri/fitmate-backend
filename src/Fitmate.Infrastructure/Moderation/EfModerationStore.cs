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

    /// <summary>BE-211: tìm Open report trùng (chống spam DEC-028).</summary>
    public async Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(
            x => x.ReporterId == reporterId
              && x.TargetType == targetType
              && x.TargetId == targetId
              && x.Status == ReportStatus.Pending, ct);
        return r is null ? null : ToRow(r);
    }

    public async Task SaveReportAsync(ReportRow row, CancellationToken ct)
    {
        var entity = await _db.Reports.FirstOrDefaultAsync(r => r.Id == row.Id, ct)
            ?? throw new InvalidOperationException($"Report '{row.Id}' not found.");

        // Apply transition via domain methods (preserve state machine invariants).
        // Domain methods set AdminNote + ResolvedByAdminId + ResolveAction atomically.
        if (row.Status == ReportStatus.Resolved)
        {
            entity.Resolve(
                row.ResolvedByAdminId ?? Guid.Empty,
                row.ResolveAction ?? string.Empty,
                row.AdminNote);
        }
        else if (row.Status == ReportStatus.Dismissed)
        {
            entity.Dismiss(row.ResolvedByAdminId ?? Guid.Empty, row.AdminNote);
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>BE-211: soft-delete Post khi action=DeleteContent.</summary>
    public async Task SoftDeletePostAsync(Guid postId, CancellationToken ct)
    {
        var p = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId, ct);
        if (p is null) return;
        p.SoftDelete();
        // Do NOT save here — let caller control transaction boundary via SaveChangesAsync().
    }

    /// <summary>BE-211: soft-delete Comment khi action=DeleteContent.</summary>
    public async Task SoftDeleteCommentAsync(Guid commentId, CancellationToken ct)
    {
        var c = await _db.Comments.FirstOrDefaultAsync(x => x.Id == commentId, ct);
        if (c is null) return;
        c.SoftDelete();
        // Do NOT save here — let caller control transaction boundary.
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    private static ReportRow ToRow(Report r) => new(
        Id: r.Id,
        ReporterId: r.ReporterId,
        TargetType: r.TargetType,
        TargetId: r.TargetId,
        Reason: r.Reason,
        Details: r.Details,
        Status: r.Status,
        AdminNote: r.AdminNote,
        ResolvedByAdminId: r.ResolvedByAdminId,
        IdempotencyKey: r.IdempotencyKey,
        ResolveAction: r.ResolveAction,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);
}
