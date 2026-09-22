using Fitmate.Application.Notifications;
using Fitmate.Application.Outbox;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

/// <summary>
/// BE-181: Friend + FriendRequest use cases.
/// Tất cả mutation đều atomic: validate → write → outbox enqueue → dispatch push → SaveChangesAsync.
/// </summary>
public class FriendService : IFriendService
{
    private readonly IFriendStore _store;
    private readonly IBlockStore _blockStore;
    private readonly IUserRoleLookup _users;
    private readonly ISocialStore _socialStore;
    private readonly IOutboxDispatcher _outbox;
    private readonly IPushDeliveryDispatcher _push;

    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    public FriendService(
        IFriendStore store,
        IBlockStore blockStore,
        IUserRoleLookup users,
        ISocialStore socialStore,
        IOutboxDispatcher outbox,
        IPushDeliveryDispatcher push)
    {
        _store = store;
        _blockStore = blockStore;
        _users = users;
        _socialStore = socialStore;
        _outbox = outbox;
        _push = push;
    }

    // ─── Use cases ──────────────────────────────────────────────────────────

    public async Task<FriendRequestView> SendRequestAsync(Guid senderId, Guid recipientId, CancellationToken ct)
    {
        EnsureAuthenticated(senderId);
        if (senderId == recipientId)
            throw new ValidationException(ErrorCodes.Social.CannotSelfFriend,
                "Cannot send friend request to self.",
                new Dictionary<string, string[]> { ["recipientId"] = new[] { "self" } });
        if (!await _users.UserExistsAsync(recipientId, ct))
            throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User '{recipientId}' not found.");

        // Block theo cả 2 chiều → sender không gửi được.
        if (await _blockStore.IsBlockedEitherWayAsync(senderId, recipientId, ct))
            throw new ValidationException(ErrorCodes.Social.RecipientBlocked,
                "Cannot send friend request: blocked relation exists.",
                new Dictionary<string, string[]> { ["recipientId"] = new[] { "blocked" } });

        // Đã là bạn?
        if (await _store.FriendshipExistsAsync(senderId, recipientId, ct))
            throw new ConflictException(ErrorCodes.Social.AlreadyFriends, "Already friends.");

        // B đã có pending request gửi cho A → không cho A tạo duplicate; trả trạng thái để FE điều hướng accept.
        var reversePending = await _store.FindPendingRequestBetweenAsync(recipientId, senderId, ct);
        if (reversePending is not null)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyExists,
                "There is a pending request from the recipient; accept that one instead.");

        // A đã có pending request gửi cho B → idempotent, trả request cũ.
        var existing = await _store.FindPendingRequestBetweenAsync(senderId, recipientId, ct);
        if (existing is not null)
            return ToView(existing);

        var request = FriendRequest.Create(senderId, recipientId);
        await _store.AddFriendRequestAsync(request, ct);

        _outbox.Enqueue("friend.request.created", new
        {
            requestId = request.Id,
            senderId,
            recipientId,
            createdAt = request.CreatedAt
        });

        // Enqueue push notification (BE-200: in-app + optional push delivery).
        var senderAuthor = await LookupAuthorAsync(senderId, ct);
        var senderName = senderAuthor?.DisplayName ?? "Someone";
        var pushPayload = new NotificationPayload(
            recipientId,
            NotificationType.FriendRequest,
            $"Friend request from {senderName}",
            null,
            new { requestId = request.Id, senderId },
            request.Id,
            "friend_request_sent");
        await _push.DispatchAsync(pushPayload, ct);

        await _store.SaveChangesAsync(ct);
        return ToView(request);
    }

    public async Task AcceptRequestAsync(Guid actorId, Guid requestId, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);

        var request = await _store.FindRequestByIdAsync(requestId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.FriendRequestAlreadyProcessed, $"Request '{requestId}' not found.");

        // Authorization: chỉ recipient accept.
        if (request.RecipientId != actorId)
            throw new ForbiddenException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Only the recipient can accept this request.");

        // Status guard.
        if (request.Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Request already processed.");

        // Block re-check ngay trước khi mutate (race: block xảy ra sau khi request được tạo).
        if (await _blockStore.IsBlockedEitherWayAsync(request.SenderId, request.RecipientId, ct))
        {
            // Theo block policy: block cancel pending request. Reject silent.
            request.Reject();
            await _store.UpdateFriendRequestAsync(request, ct);
            await _store.SaveChangesAsync(ct);
            return;
        }

        // Đã là bạn (race): vẫn mark accepted nhưng không tạo row duplicate.
        request.Accept();
        await _store.UpdateFriendRequestAsync(request, ct);

        if (!await _store.FriendshipExistsAsync(request.SenderId, request.RecipientId, ct))
        {
            var f = Friendship.Create(request.SenderId, request.RecipientId);
            await _store.AddFriendshipAsync(f, ct);
        }

        _outbox.Enqueue("friend.request.accepted", new
        {
            requestId = request.Id,
            senderId = request.SenderId,
            recipientId = request.RecipientId,
            acceptedAt = request.UpdatedAt
        });

        // Enqueue push notification: tell sender that their request was accepted (BE-200).
        var actorAuthor = await LookupAuthorAsync(actorId, ct);
        var actorName = actorAuthor?.DisplayName ?? "Someone";
        var pushPayload = new NotificationPayload(
            request.SenderId,
            NotificationType.FriendRequest,
            $"{actorName} accepted your friend request",
            null,
            new { requestId = request.Id, senderId = request.SenderId },
            request.Id,
            "friend_request_accepted");
        await _push.DispatchAsync(pushPayload, ct);

        await _store.SaveChangesAsync(ct);
    }

    public async Task RejectRequestAsync(Guid actorId, Guid requestId, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);

        var request = await _store.FindRequestByIdAsync(requestId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.FriendRequestAlreadyProcessed, $"Request '{requestId}' not found.");

        if (request.RecipientId != actorId)
            throw new ForbiddenException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Only the recipient can reject this request.");
        if (request.Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Request already processed.");

        request.Reject();
        await _store.UpdateFriendRequestAsync(request, ct);

        // BE-181 quyết định: friend.request.rejected KHÔNG gửi notification cho sender.
        // Outbox không enqueue để tránh consumer tạo event thừa.

        await _store.SaveChangesAsync(ct);
    }

    public async Task CancelRequestAsync(Guid actorId, Guid requestId, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);

        var request = await _store.FindRequestByIdAsync(requestId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.FriendRequestAlreadyProcessed, $"Request '{requestId}' not found.");

        if (request.SenderId != actorId)
            throw new ForbiddenException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Only the sender can cancel this request.");
        if (request.Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Request already processed.");

        request.Cancel();
        await _store.UpdateFriendRequestAsync(request, ct);
        await _store.SaveChangesAsync(ct);
    }

    public async Task UnfriendAsync(Guid actorId, Guid otherUserId, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);
        if (actorId == otherUserId)
            throw new ValidationException(ErrorCodes.Social.CannotSelfFriend,
                "Cannot unfriend self.",
                new Dictionary<string, string[]> { ["otherUserId"] = new[] { "self" } });

        // Idempotent: nếu không còn là bạn → no-op success.
        if (!await _store.FriendshipExistsAsync(actorId, otherUserId, ct))
            return;

        await _store.RemoveFriendshipAsync(actorId, otherUserId, ct);

        _outbox.Enqueue("friendship.removed", new
        {
            actorId,
            otherUserId,
            removedAt = DateTime.UtcNow
        });

        await _store.SaveChangesAsync(ct);
    }

    public async Task<FriendPage> ListFriendsAsync(Guid userId, string? cursor, int limit, CancellationToken ct)
    {
        EnsureAuthenticated(userId);
        if (limit < 1) limit = DefaultLimit;
        if (limit > MaxLimit) limit = MaxLimit;

        var (createdAt, id) = ParseCursor(cursor);
        var (ids, next) = await _store.ListFriendIdsAsync(userId, createdAt, id, limit + 1, ct);

        var hasMore = ids.Count > limit;
        var page = hasMore ? ids.Take(limit).ToList() : ids;

        // Lookup author/profile — dùng ISocialStore.GetAuthorAsync qua FriendService.
        // Để tránh phụ thuộc vòng, dùng helper thông qua _store (sẽ được mở rộng).
        var items = new List<FriendSummary>(page.Count);
        foreach (var fid in page)
        {
            var author = await LookupAuthorAsync(fid, ct);
            items.Add(new FriendSummary(fid, author?.DisplayName ?? "Unknown", author?.AvatarUrl, DateTime.UtcNow));
        }
        return new FriendPage(items, next);
    }

    public Task<bool> IsFriendAsync(Guid a, Guid b, CancellationToken ct)
        => _store.FriendshipExistsAsync(a, b, ct);

    public async Task<FriendRequestStatusView> GetRelationStatusAsync(Guid viewerId, Guid otherUserId, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);
        if (viewerId == otherUserId)
            return new FriendRequestStatusView("none", null, null);

        if (await _blockStore.IsBlockedEitherWayAsync(viewerId, otherUserId, ct))
            return new FriendRequestStatusView("blocked_either", null, null);

        if (await _store.FriendshipExistsAsync(viewerId, otherUserId, ct))
            return new FriendRequestStatusView("friends", null, null);

        var outgoing = await _store.FindPendingRequestBetweenAsync(viewerId, otherUserId, ct);
        if (outgoing is not null)
            return new FriendRequestStatusView("outgoing_pending", outgoing.Id, outgoing.CreatedAt);

        var incoming = await _store.FindPendingRequestBetweenAsync(otherUserId, viewerId, ct);
        if (incoming is not null)
            return new FriendRequestStatusView("incoming_pending", incoming.Id, incoming.CreatedAt);

        return new FriendRequestStatusView("none", null, null);
    }

    public async Task<IReadOnlyList<FriendRequestDetailedView>> ListPendingRequestsAsync(Guid userId, string type, CancellationToken ct)
    {
        EnsureAuthenticated(userId);
        var requests = await _store.ListPendingRequestsAsync(userId, type, ct);
        var results = new List<FriendRequestDetailedView>(requests.Count);
        foreach (var r in requests)
        {
            var senderAuthor = await LookupAuthorAsync(r.SenderId, ct);
            var recipAuthor = await LookupAuthorAsync(r.RecipientId, ct);
            var senderSummary = new FriendSummary(r.SenderId, senderAuthor?.DisplayName ?? "Unknown", senderAuthor?.AvatarUrl, r.CreatedAt);
            var recipSummary = new FriendSummary(r.RecipientId, recipAuthor?.DisplayName ?? "Unknown", recipAuthor?.AvatarUrl, r.CreatedAt);
            results.Add(new FriendRequestDetailedView(r.Id, r.SenderId, r.RecipientId, r.Status.ToString().ToLowerInvariant(), r.CreatedAt, senderSummary, recipSummary));
        }
        return results;
    }

    private static void EnsureAuthenticated(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
    }

    private static FriendRequestView ToView(FriendRequest r) =>
        new(r.Id, r.SenderId, r.RecipientId, r.Status.ToString().ToLowerInvariant(), r.CreatedAt);

    private static (DateTime? createdAt, Guid? id) ParseCursor(string? cursor)
        => CursorEncoder.Decode(cursor);

    /// <summary>Bridge sang social store để tra tên/avatar.</summary>
    private Task<SocialAuthorRow?> LookupAuthorAsync(Guid userId, CancellationToken ct)
        => _socialStore.GetAuthorAsync(userId, ct);
}
