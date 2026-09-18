using Fitmate.Domain.Identity;

namespace Fitmate.Application.Auth;

/// <summary>
/// Application-layer authorization guards. Authoritative check cho role + ownership.
/// Controller chỉ là 1 lớp filter UI; Application services PHẢI gọi guard trước khi mutate.
/// </summary>
public interface IAuthorizationGuard
{
    Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct);

    /// <summary>
    /// Bắt buộc user phải có ít nhất 1 trong các role.
    /// </summary>
    Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct);

    /// <summary>
    /// Bắt buộc user phải là "owner" — tùy use case (PT của booking, chủ certificate, chủ service).
    /// </summary>
    Task EnsureOwnerAsync(string resource, Guid userId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Trả về role của user (cache/lookup tránh gọi DB lặp lại).
    /// </summary>
    Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Kiểm tra nhanh (không throw) — cho controller dùng trước khi vào service (UX early-return).
    /// </summary>
    Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct);
}
