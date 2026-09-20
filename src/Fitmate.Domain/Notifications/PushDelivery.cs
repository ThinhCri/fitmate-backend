using Fitmate.Domain.Common;

namespace Fitmate.Domain.Notifications;

/// <summary>
/// One row per (Notification, Device) target. The worker reads and updates
/// this row independently for each device; one device's failure does not
/// affect the others (DEC-041).
/// </summary>
/// <remarks>
/// All time fields are UTC.
/// </remarks>
public class PushDelivery : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid DeviceId { get; private set; }

    public PushDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? LastAttemptAt { get; private set; }

    /// <summary>Time at which the worker is allowed to attempt again. Null = no scheduled retry.</summary>
    public DateTime? NextAttemptAt { get; private set; }

    /// <summary>Set when the provider returns success.</summary>
    public DateTime? SentAt { get; private set; }

    /// <summary>
    /// Absolute expiry. Calculated from the originating event time (see
    /// <c>PushTtlOptions</c>), NOT from the retry/defer timestamp. Once this
    /// time passes without <see cref="Status"/> reaching <see cref="PushDeliveryStatus.Sent"/>,
    /// the sweep job (or the sender check) transitions the row to <see cref="PushDeliveryStatus.Expired"/>.
    /// </summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency token used by the sweep job so two sweepers
    /// running in parallel cannot claim the same row. Updated by
    /// <see cref="MarkSweeping"/> and reset by <see cref="MarkSwept"/>.
    /// </summary>
    public DateTime? SweepClaimAt { get; private set; }

    /// <summary>
    /// Truncated failure reason. Never log the raw token, payload, or any
    /// header / body content from the provider. Allowed values are stable
    /// short strings (<c>expired</c>, <c>blocked_before_dispatch</c>, ...).
    /// </summary>
    public string? LastError { get; private set; }

    private PushDelivery() { }

    /// <summary>
    /// Create a new pending delivery. <paramref name="expiresAt"/> is computed
    /// by the caller from the originating event time and the per-type TTL
    /// (see <c>PushTtlOptions</c>) — it MUST NOT be calculated from "now" so
    /// that the TTL does not extend on every retry.
    /// </summary>
    public static PushDelivery Create(
        Guid notificationId,
        Guid deviceId,
        DateTime expiresAt,
        DateTime? nextAttemptAt = null)
    {
        if (notificationId == Guid.Empty) throw new ArgumentException("NotificationId must not be empty", nameof(notificationId));
        if (deviceId == Guid.Empty) throw new ArgumentException("DeviceId must not be empty", nameof(deviceId));
        if (expiresAt <= DateTime.UtcNow) throw new ArgumentException("ExpiresAt must be in the future", nameof(expiresAt));

        return new PushDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            DeviceId = deviceId,
            Status = PushDeliveryStatus.Pending,
            Attempts = 0,
            NextAttemptAt = nextAttemptAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Create a delivery that is already expired (for testing the sweep service).
    /// Unlike Create(), this bypasses the expiresAt future validation.
    /// </summary>
    public static PushDelivery CreateExpired(Guid notificationId, Guid deviceId, DateTime expiresAt)
    {
        if (notificationId == Guid.Empty) throw new ArgumentException("NotificationId must not be empty", nameof(notificationId));
        if (deviceId == Guid.Empty) throw new ArgumentException("DeviceId must not be empty", nameof(deviceId));

        return new PushDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            DeviceId = deviceId,
            Status = PushDeliveryStatus.Pending,
            Attempts = 0,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>Provider returned success.</summary>
    public void MarkSent()
    {
        if (Status is PushDeliveryStatus.Sent) return;
        Status = PushDeliveryStatus.Sent;
        SentAt = DateTime.UtcNow;
        LastAttemptAt = DateTime.UtcNow;
        Attempts++;
        LastError = null;
        Touch();
    }

    /// <summary>Provider returned a retryable error. Schedule the next attempt if still within TTL.</summary>
    public void MarkRetryable(string reason, DateTime? nextAttemptAt)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason must not be empty", nameof(reason));
        Status = PushDeliveryStatus.FailedRetryable;
        LastAttemptAt = DateTime.UtcNow;
        Attempts++;
        LastError = Truncate(reason);
        NextAttemptAt = nextAttemptAt;
        Touch();
    }

    /// <summary>Provider returned permanent failure. Stop retrying.</summary>
    public void MarkPermanent(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason must not be empty", nameof(reason));
        Status = PushDeliveryStatus.FailedPermanent;
        LastAttemptAt = DateTime.UtcNow;
        Attempts++;
        LastError = Truncate(reason);
        NextAttemptAt = null;
        Touch();
    }

    /// <summary>Provider says the token is no longer valid. Worker should also revoke the device.</summary>
    public void MarkTokenInvalid()
    {
        Status = PushDeliveryStatus.TokenInvalid;
        LastAttemptAt = DateTime.UtcNow;
        Attempts++;
        LastError = "token_invalid";
        NextAttemptAt = null;
        Touch();
    }

    /// <summary>Provider temporarily unavailable (e.g. maintenance window). Defer.</summary>
    public void MarkDeferred(string reason, DateTime? nextAttemptAt)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason must not be empty", nameof(reason));
        Status = PushDeliveryStatus.Deferred;
        LastAttemptAt = DateTime.UtcNow;
        Attempts++;
        LastError = Truncate(reason);
        NextAttemptAt = nextAttemptAt;
        Touch();
    }

    /// <summary>Sweep claimed the row for expiry processing. Idempotent on already-claimed rows.</summary>
    public void MarkSweeping()
    {
        if (Status is PushDeliveryStatus.Sweeping) return;
        Status = PushDeliveryStatus.Sweeping;
        SweepClaimAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>Sweep finished: TTL elapsed, no send.</summary>
    public void MarkExpired()
    {
        Status = PushDeliveryStatus.Expired;
        LastError = "expired";
        NextAttemptAt = null;
        Touch();
    }

    /// <summary>Check whether the absolute expiry has been reached.</summary>
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAt;

    private static string Truncate(string reason) => reason.Length > 200 ? reason[..200] : reason;
}
