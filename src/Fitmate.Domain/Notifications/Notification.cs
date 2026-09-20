using Fitmate.Domain.Common;

namespace Fitmate.Domain.Notifications;

/// <summary>
/// BE-200 notifications: source tracking fields are populated when a notification
/// is created from an outbox event (friend request, moderation warning, ...).
/// Combined with the partial unique index on (UserId, SourceEventId, SourceType)
/// this gives idempotency when the same domain event is replayed.
/// </summary>
public class Notification : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>1..120 chars.</summary>
    public string Title { get; private set; } = null!;

    /// <summary>1..500 chars.</summary>
    public string Message { get; private set; } = null!;

    /// <summary>Optional short text used for lock-screen payloads (&lt;=120 chars).</summary>
    public string? Summary { get; private set; }

    /// <summary>
    /// Serialized JSON payload (&lt;=4 KiB UTF-8). Field allowlist enforced by
    /// Application layer (PushPayload); the schema depends on <see cref="Type"/>.
    /// </summary>
    public string? PayloadJson { get; private set; }

    public bool Read { get; private set; }

    /// <summary>
    /// Outbox message id or domain event id that originated this notification.
    /// Used for idempotency together with <see cref="SourceType"/>.
    /// </summary>
    public Guid? SourceEventId { get; private set; }

    /// <summary>e.g. "friend", "moderation". Free-form short string.</summary>
    public string? SourceType { get; private set; }

    private Notification() { }

    /// <summary>
    /// Create a brand-new notification. The (UserId, SourceEventId, SourceType)
    /// tuple must be unique within non-null source values; callers are expected
    /// to detect and skip duplicates (the partial unique index is the database
    /// safety net).
    /// </summary>
    public static Notification Create(
        Guid userId,
        NotificationType type,
        string title,
        string message,
        string? summary,
        string? payloadJson,
        Guid? sourceEventId,
        string? sourceType)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId must not be empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title must not be empty", nameof(title));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message must not be empty", nameof(message));
        if (title.Length > 120) throw new ArgumentException("Title exceeds 120 chars", nameof(title));
        if (message.Length > 500) throw new ArgumentException("Message exceeds 500 chars", nameof(message));
        if (summary is { Length: > 120 }) throw new ArgumentException("Summary exceeds 120 chars", nameof(summary));

        return new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            Summary = summary,
            PayloadJson = payloadJson,
            SourceEventId = sourceEventId,
            SourceType = sourceType,
            Read = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    public void MarkRead()
    {
        if (Read) return; // idempotent
        Read = true;
        Touch();
    }
}
