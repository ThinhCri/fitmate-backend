using Fitmate.Application.Auth;

namespace Fitmate.UnitTests;

/// <summary>
/// Stub <see cref="IAuthorizationGuard"/> cho unit-test hiện tại — chấp nhận mọi role/owner.
/// Các test muốn verify deny sẽ dùng <see cref="RoleRequireAuthz"/> hoặc <see cref="OwnerRequireAuthz"/>.
/// </summary>
public sealed class AlwaysAllowAuthz : IAuthorizationGuard
{
    public static AlwaysAllowAuthz Instance { get; } = new();
    private AlwaysAllowAuthz() { }

    public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureOwnerAsync(string resource, Guid userId, Guid ownerUserId, CancellationToken ct) => Task.CompletedTask;
    public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>("PT");
    public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(true);
}

/// <summary>Stub guard chỉ chấp nhận user có role cho trước; nếu không phù hợp throw ForbiddenException.</summary>
public sealed class RoleRequireAuthz : IAuthorizationGuard
{
    private readonly string _requiredRole;
    public RoleRequireAuthz(string requiredRole) { _requiredRole = requiredRole; }

    public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        if (!string.Equals(role, _requiredRole, StringComparison.OrdinalIgnoreCase))
            throw new Fitmate.Domain.Common.ForbiddenException("forbidden", $"Requires role {role}.");
        return Task.CompletedTask;
    }
    public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
    public Task EnsureOwnerAsync(string resource, Guid userId, Guid ownerUserId, CancellationToken ct)
        => throw new Fitmate.Domain.Common.ForbiddenException("forbidden", "Not owner.");
    public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(_requiredRole);
    public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(true);
}
