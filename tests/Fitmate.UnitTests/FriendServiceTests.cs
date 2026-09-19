using Fitmate.Application.Moderation;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Outbox;
using Fitmate.Domain.Social;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-181: Friend + Block use cases — kiểm thử atomic (friend + outbox), race giữa
/// accept/block, idempotency unfriend, block policy huỷ pending request + friendship.
/// </summary>
public class FriendServiceTests
{
    private sealed class InMemoryFriendStore : IFriendStore
    {
        public Dictionary<(Guid, Guid), Friendship> Friendships { get; } = new();
        public List<FriendRequest> Requests { get; } = new();

        public Task<bool> FriendshipExistsAsync(Guid a, Guid b, CancellationToken ct)
        {
            var (low, high) = Friendship.CanonicalPair(a, b);
            return Task.FromResult(Friendships.ContainsKey((low, high)));
        }
        public Task AddFriendshipAsync(Friendship f, CancellationToken ct)
        {
            Friendships[(f.UserLowId, f.UserHighId)] = f;
            return Task.CompletedTask;
        }
        public Task<bool> RemoveFriendshipAsync(Guid a, Guid b, CancellationToken ct)
        {
            var (low, high) = Friendship.CanonicalPair(a, b);
            return Task.FromResult(Friendships.Remove((low, high)));
        }
        public Task<FriendRequest?> FindPendingRequestBetweenAsync(Guid s, Guid r, CancellationToken ct)
            => Task.FromResult(Requests.FirstOrDefault(x => x.SenderId == s && x.RecipientId == r && x.Status == FriendRequestStatus.Pending));
        public Task<FriendRequest?> FindRequestByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Requests.FirstOrDefault(x => x.Id == id));
        public Task AddFriendRequestAsync(FriendRequest req, CancellationToken ct) { Requests.Add(req); return Task.CompletedTask; }
        public Task UpdateFriendRequestAsync(FriendRequest req, CancellationToken ct) { return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<(IReadOnlyList<Guid>, string?)> ListFriendIdsAsync(Guid u, DateTime? c, Guid? id, int limit, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Guid>)new List<Guid>(), (string?)null));
    }

    private sealed class InMemoryBlockStore : IBlockStore
    {
        public Dictionary<(Guid, Guid), BlockedUser> Blocks { get; } = new();

        public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct)
            => Task.FromResult(Blocks.ContainsKey((a, b)) || Blocks.ContainsKey((b, a)));
        public Task<bool> BlockExistsAsync(Guid blocker, Guid blocked, CancellationToken ct)
            => Task.FromResult(Blocks.ContainsKey((blocker, blocked)));
        public Task AddBlockAsync(BlockedUser block, CancellationToken ct) { Blocks[(block.BlockerId, block.BlockedId)] = block; return Task.CompletedTask; }
        public Task<bool> RemoveBlockAsync(Guid blocker, Guid blocked, CancellationToken ct)
            => Task.FromResult(Blocks.Remove((blocker, blocked)));
        public Task<(IReadOnlyList<Guid>, string?)> ListBlockedIdsAsync(Guid b, DateTime? c, Guid? id, int l, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Guid>)new List<Guid>(), (string?)null));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryUserLookup : IUserRoleLookup
    {
        public HashSet<Guid> Existing { get; } = new();
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct)
            => Task.FromResult(Existing.Contains(userId));
        public Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct) => Task.FromResult(false);
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<string?>("Member");
    }

    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<(string Type, string Payload)> Enqueued { get; } = new();
        public void Enqueue(string type, object payload)
        {
            Enqueued.Add((type, payload?.ToString() ?? ""));
        }
    }

    private sealed class InMemorySocialStore : ISocialStore
    {
        public Task<SocialAuthorRow?> GetAuthorAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<SocialAuthorRow?>(new SocialAuthorRow(userId, "Test User", null));
        // Các method khác không dùng trong Friend tests — throw để bắt nếu ai gọi nhầm.
        public Task<(IReadOnlyList<SocialPostRow>, string?)> ListPostsAsync(Guid? v, FeedScope s, DateTime? c, Guid? i, int l, CancellationToken ct) => throw new NotImplementedException();
        public Task<SocialPostRow?> GetPostByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<SocialPostRow?> GetPostWithAuthorAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task AddPostAsync(SocialPostRow r, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> RemovePostAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> PostExistsAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task AddCommentAsync(SocialCommentRow r, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasReactionAsync(Guid p, Guid u, CancellationToken ct) => throw new NotImplementedException();
        public Task AddReactionAsync(SocialReactionRow r, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> ChangePostReactionsAsync(Guid id, int d, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> ChangePostCommentsAsync(Guid id, int d, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> TargetExistsAsync(ReportTargetType k, Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<Guid> AddReportAsync(Guid r, ReportTargetType k, Guid id, string reason, string? d, string? k2, CancellationToken ct) => throw new NotImplementedException();
        public Task<SocialCommentRow?> GetCommentAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<ReportRow?> FindOpenReportAsync(Guid r, ReportTargetType t, Guid id, CancellationToken ct) => Task.FromResult<ReportRow?>(null);
        public Task<ReportRow?> FindOpenReportByKeyAsync(Guid r, string k, CancellationToken ct) => Task.FromResult<ReportRow?>(null);
    }

    private static FriendService NewService(InMemoryFriendStore fs, InMemoryBlockStore bs, InMemoryUserLookup ul, CapturingOutbox ob, InMemorySocialStore ss)
        => new(fs, bs, ul, ss, ob);

    [Fact]
    public async Task SendRequest_ToSelf_ThrowsValidation()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var id = Guid.NewGuid();
        u.Existing.Add(id);
        var svc = NewService(f, b, u, ob, s);
        var act = () => svc.SendRequestAsync(id, id, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Social.CannotSelfFriend);
    }

    [Fact]
    public async Task SendRequest_UnknownRecipient_ThrowsNotFound()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var svc = NewService(f, b, u, ob, s);
        var act = () => svc.SendRequestAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Identity.UserNotFound);
    }

    [Fact]
    public async Task SendRequest_BlockedRelation_ThrowsValidation()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var sender = Guid.NewGuid(); var recip = Guid.NewGuid();
        u.Existing.Add(recip);
        b.Blocks[(recip, sender)] = BlockedUser.Create(recip, sender);
        var svc = NewService(f, b, u, ob, s);
        var act = () => svc.SendRequestAsync(sender, recip, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Social.RecipientBlocked);
    }

    [Fact]
    public async Task SendRequest_AlreadyFriends_ThrowsConflict()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(c);
        f.Friendships[Friendship.CanonicalPair(a, c)] = Friendship.Create(a, c);
        var svc = NewService(f, b, u, ob, s);
        var act = () => svc.SendRequestAsync(a, c, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Social.AlreadyFriends);
    }

    [Fact]
    public async Task SendRequest_CrossRequestAlreadyExists_ThrowsConflict()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(c);
        f.Requests.Add(FriendRequest.Create(c, a)); // C đã gửi cho A
        var svc = NewService(f, b, u, ob, s);
        var act = () => svc.SendRequestAsync(a, c, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Social.FriendRequestAlreadyExists);
    }

    [Fact]
    public async Task SendRequest_DuplicatePending_IsIdempotent()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(c);
        f.Requests.Add(FriendRequest.Create(a, c));
        var svc = NewService(f, b, u, ob, s);
        var v = await svc.SendRequestAsync(a, c, CancellationToken.None);
        f.Requests.Should().HaveCount(1);
        ob.Enqueued.Should().HaveCount(0); // idempotent không enqueue lại
    }

    [Fact]
    public async Task AcceptRequest_Atomic_CreatesFriendshipAndEnqueuesAcceptedEvent()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(a); u.Existing.Add(c);
        var req = FriendRequest.Create(a, c);
        f.Requests.Add(req);
        var svc = NewService(f, b, u, ob, s);

        await svc.AcceptRequestAsync(c, req.Id, CancellationToken.None);

        f.Friendships.Should().HaveCount(1);
        f.Requests[0].Status.Should().Be(FriendRequestStatus.Accepted);
        ob.Enqueued.Should().ContainSingle(e => e.Type == "friend.request.accepted");
    }

    [Fact]
    public async Task AcceptRequest_WrongActor_ThrowsForbidden()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid(); var stranger = Guid.NewGuid();
        u.Existing.Add(c);
        var req = FriendRequest.Create(a, c);
        f.Requests.Add(req);
        var svc = NewService(f, b, u, ob, s);

        var act = () => svc.AcceptRequestAsync(stranger, req.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task AcceptRequest_AcceptTwice_ThrowsConflict()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(c);
        var req = FriendRequest.Create(a, c);
        f.Requests.Add(req);
        var svc = NewService(f, b, u, ob, s);

        await svc.AcceptRequestAsync(c, req.Id, CancellationToken.None);
        var act = () => svc.AcceptRequestAsync(c, req.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Social.FriendRequestAlreadyProcessed);
    }

    [Fact]
    public async Task AcceptRequest_ConcurrentBlock_RejectsRequestAndNoFriendship()
    {
        // Race: tại thời điểm accept, block đã tồn tại → request bị reject silent, không tạo friendship.
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(a); u.Existing.Add(c);
        var req = FriendRequest.Create(a, c);
        f.Requests.Add(req);
        b.Blocks[(c, a)] = BlockedUser.Create(c, a); // C đã block A trước khi accept
        var svc = NewService(f, b, u, ob, s);

        await svc.AcceptRequestAsync(c, req.Id, CancellationToken.None);

        f.Friendships.Should().BeEmpty();
        f.Requests[0].Status.Should().Be(FriendRequestStatus.Rejected);
        // Không enqueue friend.request.accepted vì bị block.
        ob.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectRequest_DoesNotEnqueue()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        u.Existing.Add(c);
        var req = FriendRequest.Create(a, c);
        f.Requests.Add(req);
        var svc = NewService(f, b, u, ob, s);

        await svc.RejectRequestAsync(c, req.Id, CancellationToken.None);

        f.Requests[0].Status.Should().Be(FriendRequestStatus.Rejected);
        ob.Enqueued.Should().BeEmpty(); // không notification cho sender
    }

    [Fact]
    public async Task CancelRequest_OnlySender()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        var req = FriendRequest.Create(a, c);
        f.Requests.Add(req);
        var svc = NewService(f, b, u, ob, s);

        // recipient cố cancel → forbidden
        var act = () => svc.CancelRequestAsync(c, req.Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();

        // sender cancel OK
        await svc.CancelRequestAsync(a, req.Id, CancellationToken.None);
        f.Requests[0].Status.Should().Be(FriendRequestStatus.Cancelled);
    }

    [Fact]
    public async Task Unfriend_FromEitherSide_RemovesCanonical()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        f.Friendships[Friendship.CanonicalPair(a, c)] = Friendship.Create(a, c);
        var svc = NewService(f, b, u, ob, s);

        await svc.UnfriendAsync(c, a, CancellationToken.None); // C unfriend A
        f.Friendships.Should().BeEmpty();
        ob.Enqueued.Should().ContainSingle(e => e.Type == "friendship.removed");
    }

    [Fact]
    public async Task Unfriend_NotFriends_IsIdempotent()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var a = Guid.NewGuid(); var c = Guid.NewGuid();
        var svc = NewService(f, b, u, ob, s);
        await svc.UnfriendAsync(a, c, CancellationToken.None);
        ob.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task CanonicalPair_IsIdempotent()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var (l1, h1) = Friendship.CanonicalPair(a, b);
        var (l2, h2) = Friendship.CanonicalPair(b, a);
        l1.Should().Be(l2); h1.Should().Be(h2);
        (l1.CompareTo(h1) < 0).Should().BeTrue();
    }

    [Fact]
    public async Task CanonicalPair_SelfThrows()
    {
        var id = Guid.NewGuid();
        var act = () => Friendship.CanonicalPair(id, id);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task GetRelationStatus_None()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox(); var s = new InMemorySocialStore();
        var svc = NewService(f, b, u, ob, s);
        var v = await svc.GetRelationStatusAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        v.Relation.Should().Be("none");
    }
}

/// <summary>BE-181 Block policy.</summary>
public class BlockServiceTests
{
    private sealed class InMemoryBlockStore : IBlockStore
    {
        public Dictionary<(Guid, Guid), BlockedUser> Blocks { get; } = new();
        public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct)
            => Task.FromResult(Blocks.ContainsKey((a, b)) || Blocks.ContainsKey((b, a)));
        public Task<bool> BlockExistsAsync(Guid blocker, Guid blocked, CancellationToken ct)
            => Task.FromResult(Blocks.ContainsKey((blocker, blocked)));
        public Task AddBlockAsync(BlockedUser block, CancellationToken ct) { Blocks[(block.BlockerId, block.BlockedId)] = block; return Task.CompletedTask; }
        public Task<bool> RemoveBlockAsync(Guid blocker, Guid blocked, CancellationToken ct)
            => Task.FromResult(Blocks.Remove((blocker, blocked)));
        public Task<(IReadOnlyList<Guid>, string?)> ListBlockedIdsAsync(Guid b, DateTime? c, Guid? id, int l, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Guid>)new List<Guid>(), (string?)null));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryFriendStore : IFriendStore
    {
        public Dictionary<(Guid, Guid), Friendship> Friendships { get; } = new();
        public List<FriendRequest> Requests { get; } = new();
        public Task<bool> FriendshipExistsAsync(Guid a, Guid b, CancellationToken ct)
        {
            var (low, high) = Friendship.CanonicalPair(a, b);
            return Task.FromResult(Friendships.ContainsKey((low, high)));
        }
        public Task AddFriendshipAsync(Friendship f, CancellationToken ct) { Friendships[(f.UserLowId, f.UserHighId)] = f; return Task.CompletedTask; }
        public Task<bool> RemoveFriendshipAsync(Guid a, Guid b, CancellationToken ct)
        {
            var (low, high) = Friendship.CanonicalPair(a, b);
            return Task.FromResult(Friendships.Remove((low, high)));
        }
        public Task<FriendRequest?> FindPendingRequestBetweenAsync(Guid s, Guid r, CancellationToken ct)
            => Task.FromResult(Requests.FirstOrDefault(x => x.SenderId == s && x.RecipientId == r && x.Status == FriendRequestStatus.Pending));
        public Task<FriendRequest?> FindRequestByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Requests.FirstOrDefault(x => x.Id == id));
        public Task AddFriendRequestAsync(FriendRequest req, CancellationToken ct) { Requests.Add(req); return Task.CompletedTask; }
        public Task UpdateFriendRequestAsync(FriendRequest req, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<(IReadOnlyList<Guid>, string?)> ListFriendIdsAsync(Guid u, DateTime? c, Guid? id, int l, CancellationToken ct)
            => Task.FromResult(((IReadOnlyList<Guid>)new List<Guid>(), (string?)null));
    }

    private sealed class InMemoryUserLookup : IUserRoleLookup
    {
        public HashSet<Guid> Existing { get; } = new();
        public Task<bool> UserExistsAsync(Guid id, CancellationToken ct) => Task.FromResult(Existing.Contains(id));
        public Task<bool> IsInRoleAsync(Guid id, string roleName, CancellationToken ct) => Task.FromResult(false);
        public Task<string?> GetPrimaryRoleAsync(Guid id, CancellationToken ct) => Task.FromResult<string?>("Member");
    }

    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<string> Enqueued { get; } = new();
        public void Enqueue(string type, object payload) { Enqueued.Add(type); }
    }

    private static BlockService NewService(InMemoryFriendStore fs, InMemoryBlockStore bs, InMemoryUserLookup ul, CapturingOutbox ob)
        => new(bs, fs, ul, ob);

    [Fact]
    public async Task Block_Self_Throws()
    {
        var id = Guid.NewGuid();
        var svc = NewService(new InMemoryFriendStore(), new InMemoryBlockStore(), new InMemoryUserLookup(), new CapturingOutbox());
        var act = () => svc.BlockAsync(id, id, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Block_UnknownUser_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryFriendStore(), new InMemoryBlockStore(), new InMemoryUserLookup(), new CapturingOutbox());
        var act = () => svc.BlockAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Identity.UserNotFound);
    }

    [Fact]
    public async Task Block_Duplicate_ThrowsConflict()
    {
        var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup();
        var blocker = Guid.NewGuid(); var blocked = Guid.NewGuid();
        u.Existing.Add(blocked);
        b.Blocks[(blocker, blocked)] = BlockedUser.Create(blocker, blocked);
        var svc = NewService(new InMemoryFriendStore(), b, u, new CapturingOutbox());
        var act = () => svc.BlockAsync(blocker, blocked, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Social.AlreadyBlocked);
    }

    [Fact]
    public async Task Block_CancelsPendingRequestsAndRemovesFriendship()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox();
        var blocker = Guid.NewGuid(); var blocked = Guid.NewGuid();
        u.Existing.Add(blocked);
        f.Requests.Add(FriendRequest.Create(blocker, blocked)); // pending
        f.Requests.Add(FriendRequest.Create(blocked, blocker)); // reverse pending
        f.Friendships[Friendship.CanonicalPair(blocker, blocked)] = Friendship.Create(blocker, blocked);

        var svc = NewService(f, b, u, ob);
        await svc.BlockAsync(blocker, blocked, CancellationToken.None);

        f.Requests.All(r => r.Status == FriendRequestStatus.Cancelled).Should().BeTrue();
        f.Friendships.Should().BeEmpty();
        b.Blocks.Should().HaveCount(1);
        ob.Enqueued.Should().ContainSingle(t => t == "user.blocked");
    }

    [Fact]
    public async Task Unblock_DoesNotRestoreFriendship()
    {
        var f = new InMemoryFriendStore(); var b = new InMemoryBlockStore(); var u = new InMemoryUserLookup(); var ob = new CapturingOutbox();
        var blocker = Guid.NewGuid(); var blocked = Guid.NewGuid();
        u.Existing.Add(blocked);
        b.Blocks[(blocker, blocked)] = BlockedUser.Create(blocker, blocked);

        var svc = NewService(f, b, u, ob);
        await svc.UnblockAsync(blocker, blocked, CancellationToken.None);

        b.Blocks.Should().BeEmpty();
        f.Friendships.Should().BeEmpty(); // explicitly NOT restored
        ob.Enqueued.Should().ContainSingle(t => t == "user.unblocked");
    }

    [Fact]
    public async Task Unblock_NotBlocked_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryFriendStore(), new InMemoryBlockStore(), new InMemoryUserLookup(), new CapturingOutbox());
        var act = () => svc.UnblockAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Social.BlockNotFound);
    }
}
