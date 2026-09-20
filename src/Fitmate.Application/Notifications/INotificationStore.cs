using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application abstraction for notification persistence.
/// The caller (<see cref="NotificationService"/>) is responsible for
/// ensuring the caller is authenticated; this interface does not enforce auth.
/// </summary>
public interface INotificationStore
{
    /// <summary>List notifications with offset pagination. Items ordered CreatedAt DESC, Id DESC.</summary>
    Task<(IReadOnlyList<Notification> Items, int Total)> ListMinePagedAsync(
        Guid userId,
        bool? read,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Find a notification by id and user id. Returns null if missing or wrong owner.</summary>
    Task<Notification?> FindByIdForOwnerAsync(Guid notificationId, Guid userId, CancellationToken ct = default);

    /// <summary>Mark notification read. Idempotent.</summary>
    Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);

    /// <summary>
    /// Add a notification row. Throws if the (UserId, SourceEventId, SourceType)
    /// unique constraint is violated — callers should catch this to implement
    /// idempotent replay.
    /// </summary>
    Task AddAsync(Notification notification, CancellationToken ct = default);

    /// <summary>
    /// Check whether a notification already exists for the given source tracking
    /// tuple. Used by <see cref="IPushDeliveryDispatcher"/> to detect duplicates.
    /// </summary>
    Task<bool> ExistsBySourceAsync(Guid userId, Guid sourceEventId, string sourceType, CancellationToken ct = default);
}
