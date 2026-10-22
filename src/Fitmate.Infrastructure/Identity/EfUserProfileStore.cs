using Fitmate.Application.Identity;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Common;
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

    public async Task<bool> IsPhoneInUseAsync(Guid userId, string phone, CancellationToken ct)
    {
        var international = "+84" + phone[1..];
        var internationalNoPlus = international.Substring(1);
        return await _db.UserProfiles.AnyAsync(p => p.UserId != userId &&
            (p.Phone == phone || p.Phone == international || p.Phone == internationalNoPlus), ct)
            || await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id != userId &&
                u.Status != UserStatus.Deleted && u.PhoneNumberConfirmed &&
                (u.PhoneNumber == phone || u.PhoneNumber == international), ct);
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

    public async Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> SearchUsersAsync(Guid viewerId, string query, int limit, CancellationToken ct)
    {
        var blockedIds = _db.BlockedUsers
            .Where(block => block.BlockerId == viewerId || block.BlockedId == viewerId)
            .Select(block => block.BlockerId == viewerId ? block.BlockedId : block.BlockerId);
        var matchedUsers = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id != viewerId && u.Status == UserStatus.Active && !blockedIds.Contains(u.Id))
            .Where(u => EF.Functions.ILike(u.FullName, $"%{query}%") || (u.UserName != null && EF.Functions.ILike(u.UserName, $"%{query}%")))
            .Take(limit)
            .ToListAsync(ct);

        return await AttachProfilesAndRolesAsync(matchedUsers, limit, ct);
    }

    public async Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> ListSuggestedUsersAsync(Guid viewerId, int limit, CancellationToken ct)
    {
        var friendIds = _db.Friendships
            .Where(friend => friend.UserLowId == viewerId || friend.UserHighId == viewerId)
            .Select(friend => friend.UserLowId == viewerId ? friend.UserHighId : friend.UserLowId);
        var pendingIds = _db.FriendRequests
            .Where(request => request.Status == FriendRequestStatus.Pending
                && (request.SenderId == viewerId || request.RecipientId == viewerId))
            .Select(request => request.SenderId == viewerId ? request.RecipientId : request.SenderId);
        var blockedIds = _db.BlockedUsers
            .Where(block => block.BlockerId == viewerId || block.BlockedId == viewerId)
            .Select(block => block.BlockerId == viewerId ? block.BlockedId : block.BlockerId);

        var users = await _db.Users.AsNoTracking()
            .Where(user => user.Id != viewerId && user.Status == UserStatus.Active)
            .Where(user => !friendIds.Contains(user.Id) && !pendingIds.Contains(user.Id) && !blockedIds.Contains(user.Id))
            .OrderByDescending(user => user.LastActiveAt)
            .ThenByDescending(user => user.CreatedAt)
            .Take(Math.Min(limit * 2, 100))
            .ToListAsync(ct);

        return await AttachProfilesAndRolesAsync(users, limit, ct);
    }

    private async Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> AttachProfilesAndRolesAsync(
        IReadOnlyList<ApplicationUser> matchedUsers, int limit, CancellationToken ct)
    {

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
            if (results.Count == limit) break;
        }

        return results;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}

