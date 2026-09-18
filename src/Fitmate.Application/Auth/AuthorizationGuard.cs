using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Auth;

/// <summary>
/// Default <see cref="IAuthorizationGuard"/> impl, dùng <see cref="IUserRoleLookup"/> từ BE-040.
/// </summary>
public class AuthorizationGuard : IAuthorizationGuard
{
    private readonly IUserRoleLookup _roleLookup;

    public AuthorizationGuard(IUserRoleLookup roleLookup)
    {
        _roleLookup = roleLookup;
    }

    public async Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        var actual = await _roleLookup.GetPrimaryRoleAsync(userId, ct);
        if (!string.Equals(actual, role, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Requires role {role}.");
    }

    public async Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var actual = await _roleLookup.GetPrimaryRoleAsync(userId, ct);
        if (actual is null || !roles.Any(r => string.Equals(r, actual, StringComparison.OrdinalIgnoreCase)))
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Requires one of: {string.Join(", ", roles)}.");
    }

    public Task EnsureOwnerAsync(string resource, Guid userId, Guid ownerUserId, CancellationToken ct)
    {
        if (userId != ownerUserId)
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Not owner of {resource}.");
        return Task.CompletedTask;
    }

    public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
        => _roleLookup.GetPrimaryRoleAsync(userId, ct);

    public async Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        var actual = await _roleLookup.GetPrimaryRoleAsync(userId, ct);
        return string.Equals(actual, role, StringComparison.OrdinalIgnoreCase);
    }
}
