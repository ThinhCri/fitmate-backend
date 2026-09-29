using Fitmate.Domain.Identity;

namespace Fitmate.Application.Identity;

public enum AdminUserStatusFilter
{
    Active = 0,
    Locked = 1,
    PendingDeletion = 2
}

public sealed record AdminUserListQuery(
    string? Role,
    AdminUserStatusFilter? Status,
    string? Search,
    int Page,
    int PageSize,
    string Sort);

public sealed record AdminUserSummary(
    Guid Id,
    string? Email,
    string? FullName,
    string Role,
    bool IsLocked,
    bool EmailConfirmed,
    DateTime CreatedAtUtc);

/// <summary>
/// Admin user management use case theo authorization-matrix.md §1 + OpenAPI <c>/admin/users</c>.
/// </summary>
public interface IAdminUserService
{
    Task<(IReadOnlyList<AdminUserSummary> Items, int Total)> ListAsync(Guid adminUserId, AdminUserListQuery query, CancellationToken ct);
    Task<AdminUserSummary> GetAsync(Guid adminUserId, Guid userId, CancellationToken ct);
    Task<AdminUserSummary> SetLockAsync(Guid targetUserId, Guid adminUserId, bool locked, string reason, CancellationToken ct);
}

/// <summary>
/// Application abstraction để admin lookup + mutate Identity user.
/// Impl ở Infrastructure dùng UserManager + RoleManager.
/// </summary>
public interface IAdminUserStore
{
    Task<(IReadOnlyList<AdminUserSummary> Items, int Total)> ListPagedAsync(AdminUserListQuery query, CancellationToken ct);
    Task<AdminUserSummary?> FindSummaryAsync(Guid userId, CancellationToken ct);

    /// <summary>Set lockout + bump SecurityStamp (revoke all sessions/tokens).</summary>
    /// <returns>true nếu thao tác thành công.</returns>
    Task<bool> SetLockAsync(Guid targetUserId, DateTimeOffset? lockoutEndUtc, CancellationToken ct);

    /// <summary>Update SecurityStamp để invalidate tất cả session hiện tại (khi unlock cũng để safety).</summary>
    Task UpdateSecurityStampAsync(Guid userId, CancellationToken ct);

    /// <summary>Get primary role cho summary.</summary>
    Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct);

    /// <summary>Đếm số lượng Admin đang hoạt động (không bị khóa / chưa bị xóa) để bảo vệ last admin.</summary>
    Task<int> CountActiveAdminsAsync(CancellationToken ct);

    /// <summary>Lưu thay đổi (đôi khi cần flush).</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
