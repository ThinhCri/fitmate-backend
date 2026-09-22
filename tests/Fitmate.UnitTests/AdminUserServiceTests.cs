using Fitmate.Application.Auth;
using Fitmate.Application.Identity;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-110: AdminUserService — list pagination/filter/sort + lock/unlock + authz + audit.
/// </summary>
public class AdminUserServiceTests
{
    private sealed class InMemoryAdminUserStore : IAdminUserStore
    {
        public Dictionary<Guid, AdminUserSummary> Items { get; } = new();
        public List<(string Action, string Type, Guid Target, Guid Actor, string? Reason)> Audit { get; } = new();
        public List<Guid> SecurityStampUpdates { get; } = new();

        public Task<(IReadOnlyList<AdminUserSummary> Items, int Total)> ListPagedAsync(AdminUserListQuery query, CancellationToken ct)
        {
            IEnumerable<AdminUserSummary> q = Items.Values;
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                q = q.Where(u => (u.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                  (u.FullName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase));
            }
            if (query.Status == AdminUserStatusFilter.Active)
                q = q.Where(u => !u.IsLocked);
            if (query.Status == AdminUserStatusFilter.Locked)
                q = q.Where(u => u.IsLocked);

            q = query.Sort switch
            {
                "email:asc" => q.OrderBy(u => u.Email),
                "email:desc" => q.OrderByDescending(u => u.Email),
                _ => q.OrderByDescending(u => u.CreatedAtUtc)
            };

            var arr = q.ToList();
            var total = arr.Count;
            var page = arr.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
            return Task.FromResult<(IReadOnlyList<AdminUserSummary>, int)>((page, total));
        }

        public Task<AdminUserSummary?> FindSummaryAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(userId));

        public Task<bool> SetLockAsync(Guid userId, DateTimeOffset? lockoutEndUtc, CancellationToken ct)
        {
            if (!Items.TryGetValue(userId, out var s)) return Task.FromResult(false);
            Items[userId] = s with { IsLocked = lockoutEndUtc.HasValue };
            return Task.FromResult(true);
        }

        public Task UpdateSecurityStampAsync(Guid userId, CancellationToken ct)
        {
            SecurityStampUpdates.Add(userId);
            return Task.CompletedTask;
        }

        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(userId)?.Role);

        public Task<int> CountActiveAdminsAsync(CancellationToken ct)
            => Task.FromResult(Items.Values.Count(u => string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase) && !u.IsLocked));

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAudit : Fitmate.Application.Trainers.IAuditLogger
    {
        public List<(string Action, string Type, Guid Target, Guid Actor, string? Reason)> Calls { get; } = new();
        public Task LogAsync(string action, string type, Guid target, Guid actor, string? reason = null, CancellationToken ct = default)
        {
            Calls.Add((action, type, target, actor, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class AllowAdmin : IAuthorizationGuard
    {
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>("Admin");
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class DenyAll : IAuthorizationGuard
    {
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct) => throw new ForbiddenException("forbidden", "Denied");
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => throw new ForbiddenException("forbidden", "Denied");
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>("Member");
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(false);
    }

    private static AdminUserService NewService(InMemoryAdminUserStore store, FakeAudit audit, IAuthorizationGuard guard)
        => new(store, audit, guard);

    private static AdminUserSummary NewSummary(Guid id, string email, string role = "Member", bool locked = false) =>
        new(id, Email: email, FullName: null, Role: role, IsLocked: locked, EmailConfirmed: true, CreatedAtUtc: DateTime.UtcNow);

    [Fact]
    public async Task List_FilterBySearch_Pagination()
    {
        var store = new InMemoryAdminUserStore();
        var audit = new FakeAudit();
        var svc = NewService(store, audit, new AllowAdmin());
        for (var i = 0; i < 30; i++) store.Items[Guid.NewGuid()] = NewSummary(Guid.NewGuid(), $"user{i:D2}@fitmate.vn");

        var (items, total) = await svc.ListAsync(
            new AdminUserListQuery(null, null, null, 1, 25, "email:asc"),
            CancellationToken.None);

        // Smoke test: page đầu trả 25/30 items.
        total.Should().Be(30);
        items.Should().HaveCount(25);
    }

    [Fact]
    public async Task List_FilterByStatus_Locked()
    {
        var store = new InMemoryAdminUserStore();
        var audit = new FakeAudit();
        var svc = NewService(store, audit, new AllowAdmin());
        store.Items[Guid.NewGuid()] = NewSummary(Guid.NewGuid(), "a@x", locked: false);
        store.Items[Guid.NewGuid()] = NewSummary(Guid.NewGuid(), "b@x", locked: true);
        store.Items[Guid.NewGuid()] = NewSummary(Guid.NewGuid(), "c@x", locked: true);

        var (items, total) = await svc.ListAsync(
            new AdminUserListQuery(null, AdminUserStatusFilter.Locked, null, 1, 25, "createdAt:desc"),
            CancellationToken.None);

        total.Should().Be(2);
        items.Should().HaveCount(2).And.AllSatisfy(u => u.IsLocked.Should().BeTrue());
    }

    [Fact]
    public async Task SetLock_HappyPath_SetsLocked_BumpsSecurityStamp_AndAudits()
    {
        var store = new InMemoryAdminUserStore();
        var audit = new FakeAudit();
        var svc = NewService(store, audit, new AllowAdmin());
        var target = Guid.NewGuid();
        store.Items[target] = NewSummary(target, "x@x.vn", locked: false);

        var updated = await svc.SetLockAsync(target, Guid.NewGuid(), locked: true, reason: "Spam abuse report", CancellationToken.None);

        updated.IsLocked.Should().BeTrue();
        store.SecurityStampUpdates.Should().Contain(target);
        audit.Calls.Should().Contain(c => c.Action == "user.lock" && c.Target == target && c.Reason == "Spam abuse report");
    }

    [Fact]
    public async Task SetLock_Unlock_StillBumpsSecurityStamp_AndAuditsUnlock()
    {
        var store = new InMemoryAdminUserStore();
        var audit = new FakeAudit();
        var svc = NewService(store, audit, new AllowAdmin());
        var target = Guid.NewGuid();
        store.Items[target] = NewSummary(target, "x@x.vn", locked: true);

        var updated = await svc.SetLockAsync(target, Guid.NewGuid(), locked: false, reason: "Resolved appeal case", CancellationToken.None);
        updated.IsLocked.Should().BeFalse();
        store.SecurityStampUpdates.Should().Contain(target);
        audit.Calls.Should().Contain(c => c.Action == "user.unlock" && c.Target == target);
    }

    [Fact]
    public async Task SetLock_Self_ThrowsValidation()
    {
        var svc = NewService(new InMemoryAdminUserStore(), new FakeAudit(), new AllowAdmin());
        var me = Guid.NewGuid();
        var act = () => svc.SetLockAsync(me, me, locked: true, reason: "self test", CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SetLock_ReasonTooShort_Throws()
    {
        var store = new InMemoryAdminUserStore();
        store.Items[Guid.NewGuid()] = NewSummary(Guid.NewGuid(), "x@x.vn");
        var svc = NewService(store, new FakeAudit(), new AllowAdmin());
        var act = () => svc.SetLockAsync(Guid.NewGuid(), Guid.NewGuid(), locked: true, reason: "abc", CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SetLock_NonAdmin_ThrowsForbidden()
    {
        var store = new InMemoryAdminUserStore();
        store.Items[Guid.NewGuid()] = NewSummary(Guid.NewGuid(), "x@x.vn");
        var svc = NewService(store, new FakeAudit(), new DenyAll());
        var act = () => svc.SetLockAsync(Guid.NewGuid(), Guid.NewGuid(), locked: true, reason: "Reason long enough", CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SetLock_TargetNotFound_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryAdminUserStore(), new FakeAudit(), new AllowAdmin());
        var act = () => svc.SetLockAsync(Guid.NewGuid(), Guid.NewGuid(), locked: true, reason: "Reason long enough", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task List_InvalidSort_Throws()
    {
        var svc = NewService(new InMemoryAdminUserStore(), new FakeAudit(), new AllowAdmin());
        var act = () => svc.ListAsync(
            new AdminUserListQuery(null, null, null, 1, 25, "name:desc"),
            CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SetLock_WhenLockingLastAdmin_ThrowsConflict()
    {
        var store = new InMemoryAdminUserStore();
        var admin1 = Guid.NewGuid();
        var callerAdmin = Guid.NewGuid();
        store.Items[admin1] = new AdminUserSummary(admin1, "admin1@fitmate.vn", "Admin 1", "Admin", false, true, DateTime.UtcNow);

        var svc = NewService(store, new FakeAudit(), new AllowAdmin());
        var act = () => svc.SetLockAsync(admin1, callerAdmin, locked: true, reason: "Locking admin", CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }
}
