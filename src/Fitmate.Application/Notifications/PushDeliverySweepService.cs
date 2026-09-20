using Fitmate.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application logic for the push delivery sweep job. Runs every 30
/// minutes (see <c>docs/backend-release/decisions.md</c> DEC-041) to convert
/// rows whose <see cref="PushDelivery.ExpiresAt"/> has passed without
/// reaching <see cref="PushDeliveryStatus.Sent"/> into
/// <see cref="PushDeliveryStatus.Expired"/>.
///
/// <para>
/// The actual claim SQL uses <c>FOR UPDATE SKIP LOCKED</c> via the
/// Infrastructure implementation; multiple workers / instances can safely
/// run concurrently.
/// </para>
/// </summary>
public class PushDeliverySweepService
{
    private readonly IPushStore _store;
    private readonly INotificationAuditLogger _audit;
    private readonly ILogger<PushDeliverySweepService> _logger;
    private readonly int _batchSize;

    public PushDeliverySweepService(
        IPushStore store,
        INotificationAuditLogger audit,
        ILogger<PushDeliverySweepService> logger,
        int batchSize = 200)
    {
        _store = store;
        _audit = audit;
        _logger = logger;
        _batchSize = batchSize;
    }

    /// <summary>
    /// Run one sweep tick: claim a batch of expired-eligible rows, mark them
    /// expired, audit. Returns the number of rows expired.
    /// </summary>
    public async Task<SweepResult> SweepOnceAsync(CancellationToken ct)
    {
        var claimed = await _store.ClaimExpiredBatchAsync(_batchSize, ct);
        if (claimed == 0) return new SweepResult(0, 0);

        var due = await _store.FetchDueBatchAsync(_batchSize, ct);
        var expiredCount = 0;
        var now = DateTime.UtcNow;
        foreach (var delivery in due)
        {
            if (ct.IsCancellationRequested) break;
            if (delivery.Status != PushDeliveryStatus.Sweeping) continue;
            if (delivery.IsExpired(now))
            {
                delivery.MarkExpired();
                await _store.UpdateStatusAsync(
                    delivery.Id,
                    PushDeliveryStatus.Expired,
                    "expired",
                    nextAttemptAt: null,
                    sentAt: null,
                    ct);
                _audit.Log(
                    "push.expired",
                    delivery.NotificationId == Guid.Empty
                        ? Guid.Empty
                        : Guid.Empty, // recipient id unavailable in pure layer; rewrite at Infrastructure audit point
                    new Dictionary<string, string>
                    {
                        ["delivery_id"] = delivery.Id.ToString(),
                        ["expires_at"] = delivery.ExpiresAt.ToString("O"),
                    });
                expiredCount++;
            }
        }
        _logger.LogInformation("Sweep: claimed {Claimed} expired {Expired}", claimed, expiredCount);
        return new SweepResult(claimed, expiredCount);
    }

    public sealed record SweepResult(int Claimed, int Expired);
}
