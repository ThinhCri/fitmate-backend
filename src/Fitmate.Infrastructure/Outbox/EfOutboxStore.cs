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
