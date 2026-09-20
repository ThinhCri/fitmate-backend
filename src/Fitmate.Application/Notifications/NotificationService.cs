using System.Text.Json;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Default <see cref="INotificationService"/> impl.
/// Authorization is enforced by always filtering queries and updates by the
/// passed <c>userId</c> (extracted from the authenticated principal by the
/// caller). Cross-user access returns 404 to avoid leaking existence.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationStore _store;

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public NotificationService(INotificationStore store)
    {
        _store = store;
    }

    public async Task<NotificationPage> ListMineAsync(
        Guid userId,
        bool? read,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        ValidateUser(userId);
        ValidatePage(page, pageSize);

        var (items, total) = await _store.ListMinePagedAsync(userId, read, page, pageSize, ct);

        var views = new List<NotificationView>(items.Count);
        foreach (var n in items)
        {
            JsonElement? payload = null;
            if (!string.IsNullOrWhiteSpace(n.PayloadJson))
            {
                using var doc = JsonDocument.Parse(n.PayloadJson);
                payload = doc.RootElement.Clone();
            }
            views.Add(new NotificationView(
                n.Id,
                n.Type,
                n.Title,
                n.Message,
                n.Summary,
                payload,
                n.Read,
                n.CreatedAt));
        }
        return new NotificationPage(views, total);
    }

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        ValidateUser(userId);
        if (notificationId == Guid.Empty)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "notificationId must not be empty",
                new Dictionary<string, string[]> { ["notificationId"] = new[] { "empty" } });

        // Ownership enforced by FindByIdForOwnerAsync (returns null if not owner).
        var notification = await _store.FindByIdForOwnerAsync(notificationId, userId, ct);
        if (notification is null)
            throw new NotFoundException(
                ErrorCodes.General.NotFound,
                $"Notification '{notificationId}' not found.");

        if (notification.Read)
        {
            // Idempotent: don't issue an unnecessary update.
            return;
        }

        await _store.MarkReadAsync(notificationId, ct);
    }

    private static void ValidateUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new UnauthorizedException(
                ErrorCodes.Identity.NotAuthenticated,
                "User is not authenticated.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "page must be >= 1",
                new Dictionary<string, string[]> { ["page"] = new[] { "min 1" } });
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                $"pageSize must be in 1..{MaxPageSize}",
                new Dictionary<string, string[]> { ["pageSize"] = new[] { $"out of range 1..{MaxPageSize}" } });
    }
}
