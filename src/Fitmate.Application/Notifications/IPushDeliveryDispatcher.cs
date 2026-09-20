using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application interface for dispatching push notifications.
///
/// <para>
/// The dispatcher is called by domain-event handlers (friend request, moderation
/// warning, ...) within the same database transaction as the originating event.
/// It creates the <see cref="Notification"/> row and, if the push subsystem is
/// enabled, also creates <see cref="PushDelivery"/> rows for every active device
/// of the recipient and enqueues an outbox <c>notification.push_requested</c>
/// event.
///
/// If the push subsystem is disabled (<see cref="PushProviderConfig.Enabled"/>
/// is false), the dispatcher creates only the <c>Notification</c> row and logs
/// an audit entry <c>notification.skipped reason=push_disabled</c> — it does NOT
/// create deferred backlog.
/// </para>
///
/// <para>
/// Idempotency: when the same (SourceEventId, SourceType, RecipientId) tuple
/// is submitted twice, the second call is a no-op that returns the existing
/// notification id. The database enforces this with a partial unique index.
/// </para>
/// </summary>
public interface IPushDeliveryDispatcher
{
    /// <summary>
    /// Dispatch a push notification. Must be called within an open database
    /// transaction so that the notification row and any outbox event are committed
    /// atomically together with the originating domain operation.
    /// </summary>
    /// <param name="payload">Validated payload conforming to <c>PushPayload.Allowlist</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Id of the created (or pre-existing) notification.</returns>
    Task<Guid> DispatchAsync(NotificationPayload payload, CancellationToken ct = default);
}
