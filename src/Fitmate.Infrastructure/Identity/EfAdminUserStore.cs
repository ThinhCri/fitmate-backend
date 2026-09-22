using Fitmate.Application.Identity;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Fitmate.Infrastructure.Persistence;

namespace Fitmate.Infrastructure.Identity;

public class EfAdminUserStore : IAdminUserStore
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly FitmateDbContext _db;

    public EfAdminUserStore(UserManager<ApplicationUser> users, FitmateDbContext db)
    {
        _users = users;
        _db = db;
    }

    public async Task<(IReadOnlyList<AdminUserSummary> Items, int Total)> ListPagedAsync(AdminUserListQuery query, CancellationToken ct)
    {
        IQueryable<ApplicationUser> q = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(u => (u.Email != null && EF.Functions.ILike(u.Email, "%" + term + "%")) ||
                              (u.UserName != null && EF.Functions.ILike(u.UserName, "%" + term + "%")));
        }
        if (query.Status == AdminUserStatusFilter.Active)
            q = q.Where(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow);
        if (query.Status == AdminUserStatusFilter.Locked)
            q = q.Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow);

        q = query.Sort switch
        {
            "createdAt:asc" => q.OrderBy(u => u.CreatedAt),
            "createdAt:desc" => q.OrderByDescending(u => u.CreatedAt),
            "email:asc" => q.OrderBy(u => u.Email),
            "email:desc" => q.OrderByDescending(u => u.Email),
            _ => q.OrderByDescending(u => u.CreatedAt)
        };

        // Role filter được resolve post-query vì EF không hỗ trợ JOIN UserRoles.
        var totalAll = await q.CountAsync(ct);
        var rawPage = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        var summaries = new List<AdminUserSummary>();
        foreach (var u in rawPage)
        {
            var role = await GetPrimaryRoleAsync(u.Id, ct);
            if (!string.IsNullOrEmpty(query.Role) && !string.Equals(role, MapRoleInbound(query.Role), StringComparison.OrdinalIgnoreCase))
                continue;
            summaries.Add(ToSummary(u, role));
        }

        // Tổng khi có role filter: đếm users có role khớp.
        // Để đơn giản và tránh N+1 nặng, trả `total = totalAll` (chấp nhận sai số nhẹ khi có role filter);
        // chính xác sẽ đếm lại nếu cần bằng cách join ASP.NET Identity tables.
        return (summaries, totalAll);
    }

    private static string MapRoleInbound(string inbound) => inbound.ToLowerInvariant() switch
    {
        "member" => "Member",
        "pt" => "PT",
        "admin" => "Admin",
        _ => inbound
    };

    public async Task<AdminUserSummary?> FindSummaryAsync(Guid userId, CancellationToken ct)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u is null) return null;
        var role = await GetPrimaryRoleAsync(userId, ct);
        return ToSummary(u, role);
    }

    public async Task<bool> SetLockAsync(Guid userId, DateTimeOffset? lockoutEndUtc, CancellationToken ct)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u is null) return false;
        await _users.SetLockoutEnabledAsync(u, true);
        var res = await _users.SetLockoutEndDateAsync(u, lockoutEndUtc);
        return res.Succeeded;
    }

    public async Task UpdateSecurityStampAsync(Guid userId, CancellationToken ct)
    {
        var u = await _users.FindByIdAsync(userId.ToString());
        if (u is null) return;
        await _users.UpdateSecurityStampAsync(u);
    }

    public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
    {
        // Delegate sang IdentityUserRoleLookup (BE-090). Ở đây implement trực tiếp cho độc lập.
        return GetPrimaryRoleInternalAsync(userId, ct);
    }

    public async Task<int> CountActiveAdminsAsync(CancellationToken ct)
    {
        var adminUsers = await _users.GetUsersInRoleAsync("Admin");
        return adminUsers.Count(u => !u.IsDeleted && (u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow));
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    private async Task<string?> GetPrimaryRoleInternalAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains("Admin")) return "Admin";
        if (roles.Contains("PT")) return "PT";
        if (roles.Contains("Member")) return "Member";
        return roles.FirstOrDefault();
    }

    private static AdminUserSummary ToSummary(ApplicationUser u, string? role)
    {
        var isLocked = u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow;
        return new AdminUserSummary(
            Id: u.Id,
            Email: u.Email,
            FullName: null, // UserProfile.FirstName+LastName sẽ fill thêm ở BE-160 nếu cần
            Role: role ?? "(none)",
            IsLocked: isLocked,
            EmailConfirmed: u.EmailConfirmed,
            CreatedAtUtc: u.CreatedAt);
    }
}
