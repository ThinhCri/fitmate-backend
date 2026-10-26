namespace Fitmate.Domain.Notifications;

/// <summary>
/// Per-device push delivery status. Each row tracks a single
/// (Notification, Device) pair, so the failure of one device does not affect
/// the others (DEC-041).
/// </summary>
public enum PushDeliveryStatus
{
    /// <summary>Outbox event has been written; worker has not picked it up yet.</summary>
    Pending = 0,

    /// <summary>Provider returned success.</summary>
    Sent = 1,

    /// <summary>
    /// Provider is temporarily unavailable; allow re-try until
    /// <see cref="PushDelivery.ExpiresAt"/>. After that, sweep transitions to
    /// <see cref="Expired"/>.
    /// </summary>
    Deferred = 2,

    /// <summary>
    /// Transient failure (network, 5xx, rate limit). Allow re-try until
    /// <see cref="PushDelivery.ExpiresAt"/>; after that, sweep transitions to
    /// <see cref="Expired"/>.
    /// </summary>
    FailedRetryable = 3,

    /// <summary>
    /// Permanent failure that must not retry: missing provider config,
    /// business check failed (block, request cancelled, report dismissed,
    /// device inactive / owner mismatch, payload rejected, etc).
    /// </summary>
    FailedPermanent = 4,

    /// <summary>Provider returned token-not-registered / not-found. Device must be revoked.</summary>
    TokenInvalid = 5,

    /// <summary>Sweep job claimed the row for expiry processing.</summary>
    Sweeping = 6,

    /// <summary>Expired without being sent. Notification row remains in the in-app inbox.</summary>
    Expired = 7,
    /// <summary>Provider attempt leased for two minutes; recoverable after a worker crash.</summary>
    Sending = 8,
}
