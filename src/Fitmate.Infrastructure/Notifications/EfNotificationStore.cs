using Fitmate.Application.Notifications;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Notifications;

public class EfNotificationStore : INotificationStore
{
    private readonly FitmateDbContext _db;

    public EfNotificationStore(FitmateDbContext db) => _db = db;

    public async Task<(IReadOnlyList<Notification> Items, int Total)> ListMinePagedAsync(
        Guid userId,
        bool? read,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var q = _db.Notifications
            .Where(n => n.UserId == userId && n.DeletedAt == null);

        if (read.HasValue)
            q = q.Where(n => n.Read == read.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<Notification?> FindByIdForOwnerAsync(
        Guid notificationId, Guid userId, CancellationToken ct)
    {
        return await _db.Notifications
            .FirstOrDefaultAsync(n =>
                n.Id == notificationId &&
                n.UserId == userId &&
                n.DeletedAt == null,
                ct);
    }

    public async Task MarkReadAsync(Guid notificationId, CancellationToken ct)
    {
        // Bulk update to avoid loading the entity into memory.
        await _db.Notifications
            .Where(n => n.Id == notificationId && !n.Read)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.Read, true)
                .SetProperty(n => n.UpdatedAt, DateTime.UtcNow),
                ct);
    }

    public async Task AddAsync(Notification notification, CancellationToken ct)
    {
        await _db.Notifications.AddAsync(notification, ct);
    }

    public async Task<bool> ExistsBySourceAsync(
        Guid userId, Guid sourceEventId, string sourceType, CancellationToken ct)
    {
        return await _db.Notifications
            .AnyAsync(n =>
                n.UserId == userId &&
                n.SourceEventId == sourceEventId &&
                n.SourceType == sourceType &&
                n.DeletedAt == null,
                ct);
    }
}
