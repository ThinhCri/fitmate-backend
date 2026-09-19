using Fitmate.Application.Social;
using Fitmate.Domain.Social;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Social;

/// <summary>
/// BE-181: Block store. Đảm bảo query "IsBlockedEitherWayAsync" tối ưu qua 2 index
/// (blocker_id PK prefix + blocked_id index).
/// </summary>
public class EfBlockStore : IBlockStore
{
    private readonly FitmateDbContext _db;

    public EfBlockStore(FitmateDbContext db) { _db = db; }

    public async Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB, CancellationToken ct)
    {
        // (A blocked B) or (B blocked A)
        return await _db.BlockedUsers.AsNoTracking()
            .AnyAsync(b =>
                (b.BlockerId == userA && b.BlockedId == userB) ||
                (b.BlockerId == userB && b.BlockedId == userA), ct);
    }

    public async Task<bool> BlockExistsAsync(Guid blockerId, Guid blockedId, CancellationToken ct)
    {
        return await _db.BlockedUsers.AsNoTracking()
            .AnyAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, ct);
    }

    public async Task AddBlockAsync(BlockedUser block, CancellationToken ct)
    {
        await _db.BlockedUsers.AddAsync(block, ct);
    }

    public async Task<bool> RemoveBlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct)
    {
        var row = await _db.BlockedUsers.FirstOrDefaultAsync(
            b => b.BlockerId == blockerId && b.BlockedId == blockedId, ct);
        if (row is null) return false;
        _db.BlockedUsers.Remove(row);
        return true;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<(IReadOnlyList<Guid> BlockedIds, string? NextCursor)> ListBlockedIdsAsync(
        Guid blockerId, DateTime? cursorCreatedAt, Guid? cursorBlockedId, int limit, CancellationToken ct)
    {
        var q = _db.BlockedUsers.AsNoTracking().Where(b => b.BlockerId == blockerId);

        if (cursorCreatedAt.HasValue && cursorBlockedId.HasValue)
        {
            var c = cursorCreatedAt.Value;
            var otherId = cursorBlockedId.Value;
            q = q.Where(b => b.CreatedAt < c
                          || (b.CreatedAt == c && b.BlockedId < otherId));
        }

        var rows = await q
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.BlockedId)
            .Take(limit)
            .Select(b => new { b.BlockedId, b.CreatedAt })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.BlockedId).ToList();
        string? next = null;
        if (rows.Count == limit)
        {
            var last = rows[^1];
            next = CursorEncoder.Encode(last.CreatedAt, last.BlockedId);
        }
        return (ids, next);
    }
}
