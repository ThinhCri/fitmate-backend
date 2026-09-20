namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application interface for listing and marking notifications read.
/// Authorization (ownership) is enforced inside this service using the passed
/// <paramref name="userId"/> — callers are responsible for extracting the
/// authenticated user id from <see cref="Auth.ICurrentUserAccessor"/>.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// List notifications for the given user with optional read-filter and
    /// offset pagination. Items are ordered by <c>CreatedAt DESC, Id DESC</c>
    /// for deterministic stable ordering when multiple rows share the same
    /// timestamp.
    /// </summary>
    /// <param name="userId">Authenticated user id.</param>
    /// <param name="read">null = all, true = only read, false = only unread.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1..100).</param>
    Task<NotificationPage> ListMineAsync(
        Guid userId,
        bool? read,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Mark one notification as read. Idempotent: returns 204 even if already
    /// read. Returns <c>null</c> if the notification does not exist or belongs
    /// to another user (404 semantics, not 403 — avoids leaking existence).
    /// </summary>
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
}
