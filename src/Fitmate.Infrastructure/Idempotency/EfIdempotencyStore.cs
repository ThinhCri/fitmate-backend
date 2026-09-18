using Fitmate.Application.Idempotency;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Idempotency;

public class EfIdempotencyStore : IIdempotencyStore
{
    private readonly FitmateDbContext _db;

    public EfIdempotencyStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct)
    {
        var existing = await _db.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == key, ct);
        if (existing is null) return new IdempotencyLookup(null, false);
        return new IdempotencyLookup(existing, Conflict: !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal));
    }

    public async Task AddAsync(IdempotencyRecord record, CancellationToken ct)
    {
        await _db.IdempotencyRecords.AddAsync(record, ct);
        await _db.SaveChangesAsync(ct);
    }

    public Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct)
    {
        return _db.IdempotencyRecords
            .Where(r => r.ExpiresAt <= cutoffUtc)
            .ExecuteDeleteAsync(ct);
    }
}
