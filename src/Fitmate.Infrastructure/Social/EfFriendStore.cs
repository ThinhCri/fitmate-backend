using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Social;

/// <summary>
/// BE-181: EF Core store cho Friend + Friendship + FriendRequest.
/// Mọi write đều tracked trong DbContext hiện tại; SaveChangesAsync() ở Application
/// layer commit cùng outbox message (transaction semantics từ SaveChanges).
/// </summary>
public class EfFriendStore : IFriendStore
{
    private readonly FitmateDbContext _db;

    public EfFriendStore(FitmateDbContext db) { _db = db; }

    public async Task<bool> FriendshipExistsAsync(Guid a, Guid b, CancellationToken ct)
    {
        var (low, high) = Friendship.CanonicalPair(a, b);
        return await _db.Friendships.AsNoTracking()
            .AnyAsync(f => f.UserLowId == low && f.UserHighId == high, ct);
    }

    public async Task AddFriendshipAsync(Friendship f, CancellationToken ct)
    {
        await _db.Friendships.AddAsync(f, ct);
    }

    public async Task<bool> RemoveFriendshipAsync(Guid a, Guid b, CancellationToken ct)
    {
        var (low, high) = Friendship.CanonicalPair(a, b);
        var row = await _db.Friendships.FirstOrDefaultAsync(
            f => f.UserLowId == low && f.UserHighId == high, ct);
        if (row is null) return false;
        _db.Friendships.Remove(row);
        return true;
    }

    public async Task<FriendRequest?> FindPendingRequestBetweenAsync(Guid senderId, Guid recipientId, CancellationToken ct)
    {
        return await _db.FriendRequests.FirstOrDefaultAsync(
            r => r.SenderId == senderId
              && r.RecipientId == recipientId
              && r.Status == FriendRequestStatus.Pending,
            ct);
    }

    public Task<FriendRequest?> FindRequestByIdAsync(Guid requestId, CancellationToken ct)
        => _db.FriendRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);

    public async Task AddFriendRequestAsync(FriendRequest request, CancellationToken ct)
    {
        await _db.FriendRequests.AddAsync(request, ct);
    }

    public Task UpdateFriendRequestAsync(FriendRequest request, CancellationToken ct)
    {
        _db.FriendRequests.Update(request);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<(IReadOnlyList<Guid> FriendIds, string? NextCursor)> ListFriendIdsAsync(
        Guid userId, DateTime? cursorCreatedAt, Guid? cursorFriendId, int limit, CancellationToken ct)
    {
        // Query friendships where user is either low or high. Map to "other side" id.
        var q = _db.Friendships.AsNoTracking()
            .Where(f => f.UserLowId == userId || f.UserHighId == userId);

        // Cursor: order by CreatedAt desc, pair desc (UserLowId desc, UserHighId desc).
        if (cursorCreatedAt.HasValue && cursorFriendId.HasValue)
        {
            var c = cursorCreatedAt.Value;
            var otherId = cursorFriendId.Value;
            // (CreatedAt, UserLowId, UserHighId) strictly less than cursor tuple.
            q = q.Where(f => f.CreatedAt < c
                          || (f.CreatedAt == c && (f.UserLowId < otherId || (f.UserLowId == otherId && f.UserHighId < userId))));
        }

        var rows = await q
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.UserLowId)
            .ThenByDescending(f => f.UserHighId)
            .Take(limit)
            .Select(f => new { f.UserLowId, f.UserHighId, f.CreatedAt })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.UserLowId == userId ? r.UserHighId : r.UserLowId).ToList();

        string? next = null;
        if (rows.Count == limit)
        {
            var last = rows[^1];
            var otherId = last.UserLowId == userId ? last.UserLowId : last.UserHighId;
            next = CursorEncoder.Encode(last.CreatedAt, otherId);
        }
        return (ids, next);
    }
}
