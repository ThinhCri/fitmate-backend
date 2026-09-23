using Fitmate.Application.Outbox;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Outbox;

public class EfOutboxStore : IOutboxStore
{
    private readonly FitmateDbContext _db;

    public EfOutboxStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<OutboxMessage>> FetchDueAsync(int batchSize, DateTime nowUtc, CancellationToken ct)
    {
        return await _db.OutboxMessages
            .Where(m => m.Status == "pending" && m.NextAttemptAt <= nowUtc)
            .OrderBy(m => m.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<(IReadOnlyList<OutboxMessage> Items, int TotalCount)> FetchDeadLettersPagedAsync(int page, int pageSize, string? typeFilter, CancellationToken ct)
    {
        var query = _db.OutboxMessages.Where(m => m.Status == "dead_letter");
        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            query = query.Where(m => m.Type == typeFilter);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<OutboxMessage>> FetchDeadLettersAsync(int limit, CancellationToken ct)
    {
        return await _db.OutboxMessages
            .Where(m => m.Status == "dead_letter")
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<OutboxMessage?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct)
    {
        return await _db.OutboxMessages.CountAsync(m => m.Status == "pending", ct);
    }

    public async Task<int> GetDeadLetterCountAsync(CancellationToken ct)
    {
        return await _db.OutboxMessages.CountAsync(m => m.Status == "dead_letter", ct);
    }

    public async Task<TimeSpan?> GetOldestPendingAgeAsync(DateTime nowUtc, CancellationToken ct)
    {
        var oldestCreatedAt = await _db.OutboxMessages
            .Where(m => m.Status == "pending")
            .OrderBy(m => m.CreatedAt)
            .Select(m => (DateTime?)m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return oldestCreatedAt.HasValue ? nowUtc - oldestCreatedAt.Value : null;
    }

    public async Task<bool> TryReplayDeadLetterAsync(Guid id, Guid adminUserId, string reason, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Atomic check & update: chỉ update nếu trạng thái hiện tại chính xác là 'dead_letter'
        var affected = await _db.OutboxMessages
            .Where(m => m.Id == id && m.Status == "dead_letter")
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, "pending")
                .SetProperty(m => m.Attempts, 0)
                .SetProperty(m => m.NextAttemptAt, now), ct);

        if (affected == 0) return false;

        // Ghi nhận AuditLog bền vững
        var audit = Fitmate.Domain.Identity.AuditLog.Record(
            actorId: adminUserId,
            actorRole: "Admin",
            action: "outbox.replayed",
            targetType: "OutboxMessage",
            targetId: id,
            reason: reason,
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                replayedAt = now
            }));

        await _db.AuditLogs.AddAsync(audit, ct);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }

    public Task<int> PurgeDoneOlderThanAsync(DateTime cutoffUtc, CancellationToken ct)
    {
        return _db.OutboxMessages
            .Where(m => m.Status == "done" && m.ProcessedAt != null && m.ProcessedAt <= cutoffUtc)
            .ExecuteDeleteAsync(ct);
    }
}
