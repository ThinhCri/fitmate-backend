using Fitmate.Application.Outbox;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

/// <summary>
/// BE-181: User block. Tách khỏi moderation/report.
/// Block policy:
///   - Chặn gửi/accept friend request theo cả 2 chiều.
///   - Khi block: huỷ pending FriendRequest giữa 2 người + xoá Friendship nếu có.
///   - KHÔNG tự gửi notification cho người bị block.
///   - KHÔNG tự khôi phục friendship khi unblock.
///   - Không ảnh hưởng booking / finance / moderation.
/// </summary>
public class BlockService : IBlockService
{
    private readonly IBlockStore _blockStore;
    private readonly IFriendStore _friendStore;
    private readonly IUserRoleLookup _users;
    private readonly IOutboxDispatcher _outbox;

    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    public BlockService(
        IBlockStore blockStore,
        IFriendStore friendStore,
        IUserRoleLookup users,
        IOutboxDispatcher outbox)
    {
        _blockStore = blockStore;
        _friendStore = friendStore;
        _users = users;
        _outbox = outbox;
    }

    public async Task BlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct)
    {
        EnsureAuthenticated(blockerId);
        if (blockerId == blockedId)
            throw new ValidationException(ErrorCodes.Social.CannotSelfFriend,
                "Cannot block self.",
                new Dictionary<string, string[]> { ["blockedId"] = new[] { "self" } });
        if (!await _users.UserExistsAsync(blockedId, ct))
            throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User '{blockedId}' not found.");

        if (await _blockStore.BlockExistsAsync(blockerId, blockedId, ct))
            throw new ConflictException(ErrorCodes.Social.AlreadyBlocked, "Already blocked.");

        var block = BlockedUser.Create(blockerId, blockedId);
        await _blockStore.AddBlockAsync(block, ct);

        // Huỷ pending request giữa 2 người (cả 2 chiều).
        await CancelPendingRequestsAsync(blockerId, blockedId, ct);

        // Xoá friendship nếu có (hard delete canonical row).
        if (await _friendStore.FriendshipExistsAsync(blockerId, blockedId, ct))
            await _friendStore.RemoveFriendshipAsync(blockerId, blockedId, ct);

        // Outbox ghi nhận cho audit / đồng bộ — KHÔNG dùng để gửi notification cho người bị block.
        _outbox.Enqueue("user.blocked", new
        {
            blockerId,
            blockedId,
            at = DateTime.UtcNow
        });

        await _blockStore.SaveChangesAsync(ct);
    }

    public async Task UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct)
    {
        EnsureAuthenticated(blockerId);
        if (blockerId == blockedId)
            throw new ValidationException(ErrorCodes.Social.CannotSelfFriend,
                "Cannot unblock self.",
                new Dictionary<string, string[]> { ["blockedId"] = new[] { "self" } });

        if (!await _blockStore.BlockExistsAsync(blockerId, blockedId, ct))
            throw new NotFoundException(ErrorCodes.Social.BlockNotFound, "Block not found.");

        await _blockStore.RemoveBlockAsync(blockerId, blockedId, ct);

        // BE-181 quyết định: KHÔNG tự khôi phục friendship khi unblock.
        _outbox.Enqueue("user.unblocked", new
        {
            blockerId,
            blockedId,
            at = DateTime.UtcNow
        });

        await _blockStore.SaveChangesAsync(ct);
    }

    public async Task<BlockListPage> ListBlockedAsync(Guid blockerId, string? cursor, int limit, CancellationToken ct)
    {
        EnsureAuthenticated(blockerId);
        if (limit < 1) limit = DefaultLimit;
        if (limit > MaxLimit) limit = MaxLimit;

        var (createdAt, id) = FriendParseCursor(cursor);
        var (ids, next) = await _blockStore.ListBlockedIdsAsync(blockerId, createdAt, id, limit + 1, ct);

        var hasMore = ids.Count > limit;
        var page = hasMore ? ids.Take(limit).ToList() : ids;
        var items = page.Select(b => new BlockedSummary(b, DateTime.UtcNow)).ToList();
        return new BlockListPage(items, next);
    }

    private async Task CancelPendingRequestsAsync(Guid a, Guid b, CancellationToken ct)
    {
        var ab = await _friendStore.FindPendingRequestBetweenAsync(a, b, ct);
        if (ab is not null)
        {
            ab.Cancel();
            await _friendStore.UpdateFriendRequestAsync(ab, ct);
        }
        var ba = await _friendStore.FindPendingRequestBetweenAsync(b, a, ct);
        if (ba is not null)
        {
            ba.Cancel();
            await _friendStore.UpdateFriendRequestAsync(ba, ct);
        }
    }

    private static void EnsureAuthenticated(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
    }

    private static (DateTime?, Guid?) FriendParseCursor(string? cursor)
        => CursorEncoder.Decode(cursor);
}
