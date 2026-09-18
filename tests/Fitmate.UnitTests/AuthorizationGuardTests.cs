using Fitmate.Application.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-090: AuthorizationGuard — Verify behavior deny/allow cho role + owner checks.
/// </summary>
public class AuthorizationGuardTests
{
    private sealed class FakeUserRoleLookup : IUserRoleLookup
    {
        public HashSet<(Guid userId, string role)> Roles { get; } = new();
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct)
            => Task.FromResult(Roles.Contains((userId, roleName)));
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
        {
            if (Roles.Contains((userId, "Admin"))) return Task.FromResult<string?>("Admin");
            if (Roles.Contains((userId, "PT"))) return Task.FromResult<string?>("PT");
            if (Roles.Contains((userId, "Member"))) return Task.FromResult<string?>("Member");
            return Task.FromResult<string?>(null);
        }
    }

    [Fact]
    public async Task EnsureRole_Matches_Passes()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var u = Guid.NewGuid();
        lookup.Roles.Add((u, "PT"));

        await guard.EnsureRoleAsync(u, "PT", CancellationToken.None); // không throw
        guard.HasRoleAsync(u, "PT", CancellationToken.None).Result.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureRole_DoesNotMatch_ThrowsForbidden()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var u = Guid.NewGuid();
        lookup.Roles.Add((u, "Member"));

        var act = () => guard.EnsureRoleAsync(u, "PT", CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureAnyRole_OneMatches_Passes()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var u = Guid.NewGuid();
        lookup.Roles.Add((u, "Admin"));

        await guard.EnsureAnyRoleAsync(u, new[] { "PT", "Admin" }, CancellationToken.None);
    }

    [Fact]
    public async Task EnsureAnyRole_NoneMatch_Throws()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var u = Guid.NewGuid();
        lookup.Roles.Add((u, "Member"));

        var act = () => guard.EnsureAnyRoleAsync(u, new[] { "PT", "Admin" }, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureOwner_NotOwner_ThrowsForbidden()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var act = () => guard.EnsureOwnerAsync("Service", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task EnsureOwner_IsOwner_Passes()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var u = Guid.NewGuid();
        await guard.EnsureOwnerAsync("Service", u, u, CancellationToken.None);
    }

    [Fact]
    public async Task GetPrimaryRole_PriorityAdminOverPt()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        var u = Guid.NewGuid();
        lookup.Roles.Add((u, "PT"));
        lookup.Roles.Add((u, "Admin"));

        (await guard.GetPrimaryRoleAsync(u, CancellationToken.None)).Should().Be("Admin");
    }

    [Fact]
    public async Task GetPrimaryRole_NoRole_ReturnsNull()
    {
        var lookup = new FakeUserRoleLookup();
        var guard = new AuthorizationGuard(lookup);
        (await guard.GetPrimaryRoleAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }
}
