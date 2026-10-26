using Fitmate.Application.Notifications;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Notifications;

public class EfPushStore : IPushStore
{
    private readonly FitmateDbContext _db;
    private readonly IOptions<FcmPushOptions>? _fcm;

    public EfPushStore(FitmateDbContext db, IOptions<FcmPushOptions>? fcm = null) { _db = db; _fcm = fcm; }

    public async Task<bool> TryClaimDeliveryAsync(Guid deliveryId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.PushDeliveries.Where(d => d.Id == deliveryId && d.ExpiresAt > now && d.NextAttemptAt <= now &&
            (d.Status == PushDeliveryStatus.Pending || d.Status == PushDeliveryStatus.Deferred || d.Status == PushDeliveryStatus.FailedRetryable || d.Status == PushDeliveryStatus.Sending))
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, PushDeliveryStatus.Sending)
                .SetProperty(d => d.NextAttemptAt, now.AddMinutes(2)).SetProperty(d => d.LastAttemptAt, now)
                .SetProperty(d => d.Attempts, d => d.Attempts + 1).SetProperty(d => d.UpdatedAt, now), ct) == 1;
    }

    public async Task<IReadOnlyList<PushDelivery>> FetchDueForNotificationAsync(Guid notificationId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.PushDeliveries.AsNoTracking().Where(d => d.NotificationId == notificationId && d.NextAttemptAt <= now &&
            (d.Status == PushDeliveryStatus.Pending || d.Status == PushDeliveryStatus.Deferred || d.Status == PushDeliveryStatus.FailedRetryable || d.Status == PushDeliveryStatus.Sending))
            .OrderBy(d => d.NextAttemptAt).Take(100).ToListAsync(ct);
    }

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
                 d.Status == PushDeliveryStatus.FailedRetryable || d.Status == PushDeliveryStatus.Sending || d.Status == PushDeliveryStatus.Sweeping) &&
                (d.NextAttemptAt <= now || d.Status == PushDeliveryStatus.Sweeping))
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
            SET status = 6, sweep_claim_at = {0}, updated_at = {0}
            WHERE id IN (
                SELECT id FROM push_deliveries
                WHERE status IN (0, 2, 3, 8)   -- Pending, Deferred, FailedRetryable, expired sending lease
                  AND expires_at <= {0}
                  AND (status != 8 OR next_attempt_at <= {0})
                  AND status != 6          -- not already Sweeping
                LIMIT {1}
                FOR UPDATE SKIP LOCKED
            )", now, batchSize);
    }

    public async Task<PushProviderConfig?> GetConfigAsync(CancellationToken ct)
    {
        // Env-backed master switch in the production composition root; no credentials in the DB.
        if (_fcm is not null)
        {
            var config = PushProviderConfig.Disabled();
            if (_fcm.Value.Enabled && FcmPushProvider.ValidConfiguration(_fcm.Value)) config.Enable(PushProvider.Fcm, null);
            return config;
        }
        return await _db.PushProviderConfigs.FindAsync(new object[] { 1 }, ct);
    }
}
