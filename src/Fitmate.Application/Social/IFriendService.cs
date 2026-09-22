using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

/// <summary>
/// BE-181: Friend + Block use cases. Mọi thao tác (accept / reject / cancel / block / unblock /
/// unfriend) đều là atomic: validate + persist + outbox enqueue cùng SaveChangesAsync.
/// Service không tự gọi external; outbox worker sẽ dispatch notification (BE-200 phase).
/// </summary>
public interface IFriendService
{
    /// <summary>Send friend request. Trả FriendRequest view cho caller.</summary>
    Task<FriendRequestView> SendRequestAsync(Guid senderId, Guid recipientId, CancellationToken ct);

    /// <summary>Accept pending request. Tạo Friendship row + outbox event.</summary>
    Task AcceptRequestAsync(Guid actorId, Guid requestId, CancellationToken ct);

    /// <summary>Reject pending request. Không gửi notification cho sender.</summary>
    Task RejectRequestAsync(Guid actorId, Guid requestId, CancellationToken ct);

    /// <summary>Sender tự huỷ request đang pending. Không gửi notification cho recipient.</summary>
    Task CancelRequestAsync(Guid actorId, Guid requestId, CancellationToken ct);

    /// <summary>Hard delete 1 Friendship row canonical. Idempotent nếu không phải bạn.</summary>
    Task UnfriendAsync(Guid actorId, Guid otherUserId, CancellationToken ct);

    /// <summary>List bạn của user hiện tại — cursor pagination.</summary>
    Task<FriendPage> ListFriendsAsync(Guid userId, string? cursor, int limit, CancellationToken ct);

    /// <summary>BE-181: query dùng cho post visibility Friends. Trả true nếu (a, b) đã là bạn.</summary>
    Task<bool> IsFriendAsync(Guid a, Guid b, CancellationToken ct);

    /// <summary>BE-181: status cho client — khi A gửi B, B send lại, trả trạng thái đang có pending
    /// request ngược chiều để B dùng accept thay vì tạo mới.</summary>
    Task<FriendRequestStatusView> GetRelationStatusAsync(Guid viewerId, Guid otherUserId, CancellationToken ct);

    /// <summary>List pending friend requests (incoming, outgoing, all).</summary>
    Task<IReadOnlyList<FriendRequestDetailedView>> ListPendingRequestsAsync(Guid userId, string type, CancellationToken ct);
}

public interface IBlockService
{
    Task BlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct);
    Task UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct);
    Task<BlockListPage> ListBlockedAsync(Guid blockerId, string? cursor, int limit, CancellationToken ct);
}

public sealed record FriendRequestView(
    Guid Id,
    Guid SenderId,
    Guid RecipientId,
    string Status,
    DateTime CreatedAt);

public sealed record FriendRequestDetailedView(
    Guid Id,
    Guid SenderId,
    Guid RecipientId,
    string Status,
    DateTime CreatedAt,
    FriendSummary? Sender,
    FriendSummary? Recipient);

public sealed record FriendSummary(
    Guid Id,
    string DisplayName,
    string? AvatarUrl,
    DateTime FriendsSince);

public sealed record FriendPage(IReadOnlyList<FriendSummary> Items, string? NextCursor);

public sealed record BlockedSummary(Guid Id, DateTime BlockedAt);

public sealed record BlockListPage(IReadOnlyList<BlockedSummary> Items, string? NextCursor);

/// <summary>BE-181: trạng thái quan hệ 2 chiều cho UI.</summary>
public sealed record FriendRequestStatusView(
    string Relation, // none | outgoing_pending | incoming_pending | friends | blocked_either
    Guid? RequestId,
    DateTime? RequestCreatedAt);
