using Fitmate.Application.Notifications;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-200: NotificationService.ListMineAsync + MarkReadAsync.
/// Authorization: caller passes userId; cross-user access returns 404 (no leak).
/// Idempotency: marking already-read notification is no-op.
/// </summary>
public class NotificationServiceTests
{
    private sealed class InMemoryNotificationStore : INotificationStore
    {
        public Dictionary<Guid, Notification> ById { get; } = new();

        public Task<(IReadOnlyList<Notification>, int)> ListMinePagedAsync(
            Guid userId, bool? read, int page, int pageSize, CancellationToken ct)
        {
            var all = ById.Values
                .Where(n => n.UserId == userId && (read is null || n.Read == read))
                .OrderByDescending(n => n.CreatedAt)
                .ThenByDescending(n => n.Id)
                .ToList();
            var total = all.Count;
            var skip = Math.Max(0, (page - 1) * pageSize);
            var slice = all.Skip(skip).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<Notification>)slice, total));
        }

        public Task<Notification?> FindByIdForOwnerAsync(Guid id, Guid userId, CancellationToken ct)
        {
            var found = ById.TryGetValue(id, out var n) && n.UserId == userId ? n : null;
            return Task.FromResult(found);
        }

        public Task MarkReadAsync(Guid id, CancellationToken ct)
        {
            if (ById.TryGetValue(id, out var n)) n.MarkRead();
            return Task.CompletedTask;
        }

        public Task AddAsync(Notification n, CancellationToken ct)
        {
            ById[n.Id] = n;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsBySourceAsync(Guid userId, Guid sourceEventId, string sourceType, CancellationToken ct)
        {
            var exists = ById.Values.Any(n =>
                n.UserId == userId &&
                n.SourceEventId == sourceEventId &&
                n.SourceType == sourceType);
            return Task.FromResult(exists);
        }
    }

    private static Notification NewNotification(Guid userId, bool read = false, NotificationType type = NotificationType.System)
    {
        var n = Notification.Create(userId, type, "Title", "Message body", null, "{}", Guid.NewGuid(), "test");
        if (read) n.MarkRead();
        return n;
    }

    [Fact]
    public async Task ListMine_Empty_ReturnsEmptyPage()
    {
        var store = new InMemoryNotificationStore();
        var svc = new NotificationService(store);
        var page = await svc.ListMineAsync(Guid.NewGuid(), null, 1, 25);
        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
    }

    [Fact]
    public async Task ListMine_FiltersByReadFlag()
    {
        var user = Guid.NewGuid();
        var store = new InMemoryNotificationStore();
        var n1 = NewNotification(user, read: false);
        var n2 = NewNotification(user, read: true);
        var n3 = NewNotification(user, read: false);
        store.ById[n1.Id] = n1; store.ById[n2.Id] = n2; store.ById[n3.Id] = n3;
        var svc = new NotificationService(store);

        var unread = await svc.ListMineAsync(user, read: false, 1, 25);
        unread.Items.Should().HaveCount(2);
        unread.Items.Should().OnlyContain(x => !x.Read);

        var read = await svc.ListMineAsync(user, read: true, 1, 25);
        read.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListMine_Pagination_RespectsPageAndPageSize()
    {
        var user = Guid.NewGuid();
        var store = new InMemoryNotificationStore();
        for (int i = 0; i < 7; i++)
            store.ById[NewNotification(user).Id] = NewNotification(user);
        var svc = new NotificationService(store);

        var p1 = await svc.ListMineAsync(user, null, 1, 3);
        var p2 = await svc.ListMineAsync(user, null, 2, 3);
        var p3 = await svc.ListMineAsync(user, null, 3, 3);

        p1.Items.Should().HaveCount(3);
        p2.Items.Should().HaveCount(3);
        p3.Items.Should().HaveCount(1);
        p1.Total.Should().Be(7);

        // No id should repeat across pages.
        var allIds = p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.Id).ToList();
        allIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ListMine_EmptyUserId_ThrowsUnauthorized()
    {
        var svc = new NotificationService(new InMemoryNotificationStore());
        var act = () => svc.ListMineAsync(Guid.Empty, null, 1, 25);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ListMine_PageZero_ThrowsValidation()
    {
        var svc = new NotificationService(new InMemoryNotificationStore());
        var act = () => svc.ListMineAsync(Guid.NewGuid(), null, 0, 25);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ListMine_PageSizeOutOfRange_ThrowsValidation()
    {
        var svc = new NotificationService(new InMemoryNotificationStore());
        var act = () => svc.ListMineAsync(Guid.NewGuid(), null, 1, 200);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task MarkRead_Owned_ChangesFlag()
    {
        var user = Guid.NewGuid();
        var store = new InMemoryNotificationStore();
        var n = NewNotification(user);
        store.ById[n.Id] = n;
        var svc = new NotificationService(store);

        await svc.MarkReadAsync(user, n.Id);
        store.ById[n.Id].Read.Should().BeTrue();
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_IsIdempotent()
    {
        var user = Guid.NewGuid();
        var store = new InMemoryNotificationStore();
        var n = NewNotification(user, read: true);
        store.ById[n.Id] = n;
        var svc = new NotificationService(store);

        await svc.MarkReadAsync(user, n.Id); // should not throw
        store.ById[n.Id].Read.Should().BeTrue();
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_Throws404()
    {
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var store = new InMemoryNotificationStore();
        var n = NewNotification(owner);
        store.ById[n.Id] = n;
        var svc = new NotificationService(store);

        var act = () => svc.MarkReadAsync(attacker, n.Id);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MarkRead_UnknownId_Throws404()
    {
        var svc = new NotificationService(new InMemoryNotificationStore());
        var act = () => svc.MarkReadAsync(Guid.NewGuid(), Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MarkRead_EmptyUserId_ThrowsUnauthorized()
    {
        var svc = new NotificationService(new InMemoryNotificationStore());
        var act = () => svc.MarkReadAsync(Guid.Empty, Guid.NewGuid());
        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
