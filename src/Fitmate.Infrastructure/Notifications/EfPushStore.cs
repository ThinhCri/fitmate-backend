using Fitmate.Application.Notifications;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Notifications;

public class EfPushStore : IPushStore
{
    private readonly FitmateDbContext _db;

    public EfPushStore(FitmateDbContext db) => _db = db;

    public async Task AddAsync(PushDelivery delivery, CancellationToken ct)
    {
        await _db.PushDeliveries.AddAsync(delivery, ct);
    }

    public async Task MarkSentAsync(Guid deliveryId, CancellationToken ct)
    {
        await _db.PushDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, PushDeliveryStatus.Sent)
                .SetProperty(d => d.SentAt, DateTime.UtcNow)
                .SetProperty(d => d.LastAttemptAt, DateTime.UtcNow)
                .SetProperty(d => d.UpdatedAt, DateTime.UtcNow),
                ct);
    }

    public async Task UpdateStatusAsync(
        Guid deliveryId,
        PushDeliveryStatus status,
        string? lastError,
        DateTime? nextAttemptAt,
        DateTime? sentAt,
        CancellationToken ct)
    {
        await _db.PushDeliveries
            .Where(d => d.Id == deliveryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.LastError, lastError ?? (string?)null)
                .SetProperty(d => d.NextAttemptAt, nextAttemptAt ?? (DateTime?)null)
                .SetProperty(d => d.SentAt, sentAt ?? (DateTime?)null)
                .SetProperty(d => d.UpdatedAt, DateTime.UtcNow),
                ct);
    }

    public async Task<PushDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct)
    {
        return await _db.PushDeliveries.FindAsync(new object[] { deliveryId }, ct);
    }

    public async Task<IReadOnlyList<PushDelivery>> FetchDueBatchAsync(
        int batchSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.PushDeliveries
            .Where(d =>
                (d.Status == PushDeliveryStatus.Pending ||
                 d.Status == PushDeliveryStatus.Deferred ||
                 d.Status == PushDeliveryStatus.FailedRetryable) &&
                d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<int> ClaimExpiredBatchAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // FOR UPDATE SKIP LOCKED so multiple workers / instances don't conflict.
        return await _db.Database.ExecuteSqlRawAsync(@"
            UPDATE push_deliveries
            SET status = 4, sweep_claim_at = {0}, updated_at = {0}
            WHERE id IN (
                SELECT id FROM push_deliveries
                WHERE status IN (0, 2, 3)   -- Pending, Deferred, FailedRetryable
                  AND expires_at <= {0}
                  AND status != 6          -- not already Sweeping
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
            )", now, batchSize);
    }

    public async Task<PushProviderConfig?> GetConfigAsync(CancellationToken ct)
    {
        return await _db.PushProviderConfigs.FindAsync(new object[] { 1 }, ct);
    }
}
