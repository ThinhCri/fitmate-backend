using Fitmate.Application.Identity;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Identity;

public class EfUserProfileStore : IUserProfileStore
{
    private readonly FitmateDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public EfUserProfileStore(FitmateDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public Task<ApplicationUser?> FindUserByIdAsync(Guid userId, CancellationToken ct)
    {
        return _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    public Task<UserProfile?> FindProfileByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task AddProfileAsync(UserProfile profile, CancellationToken ct)
    {
        await _db.UserProfiles.AddAsync(profile, ct);
    }

    public async Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains("Admin")) return "Admin";
        if (roles.Contains("PT")) return "PT";
        if (roles.Contains("Member")) return "Member";
        return roles.FirstOrDefault();
    }

    public async Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> SearchUsersAsync(string query, int limit, CancellationToken ct)
    {
        var matchedUsers = await _db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.Status == Domain.Common.UserStatus.Active)
            .Where(u => EF.Functions.ILike(u.FullName, $"%{query}%") || (u.UserName != null && EF.Functions.ILike(u.UserName, $"%{query}%")))
            .Take(limit)
            .ToListAsync(ct);

        var userIds = matchedUsers.Select(u => u.Id).ToList();
        var profiles = await _db.UserProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, ct);

        var results = new List<(ApplicationUser User, UserProfile? Profile, string Role)>();
        foreach (var u in matchedUsers)
        {
            var role = await GetPrimaryRoleAsync(u.Id, ct) ?? "Member";
            // Exclude Admin from general member search for privacy
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
                continue;

            profiles.TryGetValue(u.Id, out var profile);
            results.Add((u, profile, role));
        }

        return results;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}

