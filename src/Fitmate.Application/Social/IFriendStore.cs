using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

/// <summary>
/// BE-181: Friend + Block persistence. Store là abstraction thuần; EF Core ở
/// Infrastructure cung cấp implementation. Mọi thao tác ghi đều nằm trong cùng
/// SaveChangesAsync để đảm bảo outbox commit cùng transaction với business row.
/// </summary>
public interface IFriendStore
{
    Task<bool> FriendshipExistsAsync(Guid a, Guid b, CancellationToken ct);
    Task AddFriendshipAsync(Friendship f, CancellationToken ct);
    Task<bool> RemoveFriendshipAsync(Guid a, Guid b, CancellationToken ct);

    Task<FriendRequest?> FindPendingRequestBetweenAsync(Guid senderId, Guid recipientId, CancellationToken ct);
    Task<FriendRequest?> FindRequestByIdAsync(Guid requestId, CancellationToken ct);
    Task AddFriendRequestAsync(FriendRequest request, CancellationToken ct);
    Task UpdateFriendRequestAsync(FriendRequest request, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>BE-181: pagination friends list — trả cùng pattern như BE-180 social feed.</summary>
    Task<(IReadOnlyList<Guid> FriendIds, string? NextCursor)> ListFriendIdsAsync(
        Guid userId, DateTime? cursorCreatedAt, Guid? cursorFriendId, int limit, CancellationToken ct);
}

public interface IBlockStore
{
    Task<bool> IsBlockedEitherWayAsync(Guid userA, Guid userB, CancellationToken ct);
    Task<bool> BlockExistsAsync(Guid blockerId, Guid blockedId, CancellationToken ct);
    Task AddBlockAsync(BlockedUser block, CancellationToken ct);
    Task<bool> RemoveBlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct);

    Task<(IReadOnlyList<Guid> BlockedIds, string? NextCursor)> ListBlockedIdsAsync(
        Guid blockerId, DateTime? cursorCreatedAt, Guid? cursorBlockedId, int limit, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
