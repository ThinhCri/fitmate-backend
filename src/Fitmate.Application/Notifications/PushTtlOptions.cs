using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// TTL configuration for push delivery rows, keyed by notification type.
/// TTL is computed from the <em>originating event time</em> (not "now") so that
/// retrying or deferring does not extend the expiry window.
/// </summary>
public static class PushTtlOptions
{
    /// <summary>Default TTL when no entry is found for a type.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromHours(24);

    private static readonly Dictionary<NotificationType, TimeSpan> Ttls = new()
    {
        [NotificationType.FriendRequest] = TimeSpan.FromHours(24),
        [NotificationType.System]          = TimeSpan.FromHours(24),
        [NotificationType.Booking]        = TimeSpan.FromHours(8),
        [NotificationType.Payment]        = TimeSpan.FromHours(24),
        [NotificationType.Reminder]      = TimeSpan.FromHours(24),
    };

    /// <summary>
    /// Get the TTL for a notification type.
    /// </summary>
    public static TimeSpan GetTtl(NotificationType type) =>
        Ttls.TryGetValue(type, out var ttl) ? ttl : Default;

    /// <summary>
    /// Compute the absolute expiry time from the event timestamp and TTL.
    /// </summary>
    public static DateTime ComputeExpiry(DateTime eventUtc, NotificationType type) =>
        eventUtc.Add(GetTtl(type));
}
