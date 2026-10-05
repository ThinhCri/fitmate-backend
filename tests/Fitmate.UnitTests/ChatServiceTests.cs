using Fitmate.Application.Chat;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Domain.Chat;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-191: friend chat use cases — permission, idempotency, canonical pair, read marker monotonic,
/// pagination, race transaction, history retention qua unfriend/block.
/// </summary>
public class ChatServiceTests
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Bob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Carol = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // =========== In-memory fakes ===========

    private sealed class InMemoryChatStore : IChatStore
    {
        public Dictionary<Guid, FriendConversation> Conversations { get; } = new();
        public List<ChatMessage> Messages { get; } = new();
        public List<ChatMessageReaction> Reactions { get; } = new();
        public Dictionary<(Guid, Guid), ChatThreadRead> Reads { get; } = new();

        public Task<FriendConversation> GetOrCreateConversationAsync(Guid a, Guid b, CancellationToken ct)
        {
            var (low, high) = FriendConversation.CanonicalPair(a, b);
            var existing = Conversations.Values.FirstOrDefault(c => c.UserLowId == low && c.UserHighId == high);
            if (existing is not null) return Task.FromResult(existing);
            var fresh = FriendConversation.Create(a, b);
            Conversations[fresh.Id] = fresh;
            return Task.FromResult(fresh);
        }

        public Task<FriendConversation?> FindConversationAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Conversations.TryGetValue(id, out var c) ? c : null);

        public Task<FriendConversation?> FindConversationForPairAsync(Guid a, Guid b, CancellationToken ct)
        {
            var (low, high) = FriendConversation.CanonicalPair(a, b);
            return Task.FromResult(Conversations.Values.FirstOrDefault(c => c.UserLowId == low && c.UserHighId == high));
        }

        public Task AddMessageAsync(ChatMessage m, CancellationToken ct) { Messages.Add(m); return Task.CompletedTask; }

        public Task<ChatMessage?> FindMessageByClientMessageIdAsync(Guid convId, Guid clientId, CancellationToken ct)
            => Task.FromResult(Messages.FirstOrDefault(m => m.ConversationId == convId && m.ClientMessageId == clientId));

        public Task<ChatMessage?> GetLastMessageAsync(Guid convId, CancellationToken ct)
            => Task.FromResult(Messages.Where(m => m.ConversationId == convId)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefault());

        public Task<IReadOnlyList<ConversationSummaryRow>> ListConversationsAsync(Guid userId, DateTime? at, Guid? cid, int limit, CancellationToken ct)
        {
            var q = Conversations.Values.Where(c => c.UserLowId == userId || c.UserHighId == userId)
                .OrderByDescending(c => c.LastMessageAt).ThenByDescending(c => c.Id);
            var page = q.Take(limit).ToList();
            var rows = page.Select(c =>
            {
                var last = Messages.Where(m => m.ConversationId == c.Id)
                    .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefault();
                var peer = c.UserLowId == userId ? c.UserHighId : c.UserLowId;
                var read = Reads.TryGetValue((c.Id, userId), out var r) ? r : null;
                int unread;
                if (read is null)
                    unread = Messages.Count(m => m.ConversationId == c.Id && m.SenderUserId != userId);
                else if (read.LastReadMessageId.HasValue)
                {
                    var readMsg = Messages.FirstOrDefault(m => m.Id == read.LastReadMessageId);
                    var cutoff = readMsg?.CreatedAt ?? DateTime.MinValue;
                    unread = Messages.Count(m => m.ConversationId == c.Id && m.SenderUserId != userId && m.CreatedAt > cutoff);
                }
                else unread = 0;
                return new ConversationSummaryRow(c.Id, peer, c.LastMessageAt, last, unread);
            }).ToList();
            return Task.FromResult((IReadOnlyList<ConversationSummaryRow>)rows);
        }

        public Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid convId, DateTime? at, Guid? mid, int limit, CancellationToken ct)
        {
            var q = Messages.Where(m => m.ConversationId == convId)
                .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id);
            return Task.FromResult((IReadOnlyList<ChatMessage>)q.Take(limit).ToList());
        }

        public Task<ChatMessage?> FindMessageAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Messages.FirstOrDefault(m => m.Id == id));

        public Task<ChatMessageReaction?> FindReactionAsync(Guid messageId, Guid userId, CancellationToken ct)
            => Task.FromResult(Reactions.FirstOrDefault(r => r.MessageId == messageId && r.UserId == userId));

        public Task AddReactionAsync(ChatMessageReaction reaction, CancellationToken ct)
        {
            Reactions.Add(reaction);
            var message = Messages.First(m => m.Id == reaction.MessageId);
            message.Reactions.Add(reaction);
            return Task.CompletedTask;
        }

        public void RemoveReaction(ChatMessageReaction reaction)
        {
            Reactions.Remove(reaction);
            Messages.First(m => m.Id == reaction.MessageId).Reactions.Remove(reaction);
        }

        public Task<ChatThreadRead?> GetThreadReadAsync(Guid convId, Guid userId, CancellationToken ct)
            => Task.FromResult(Reads.TryGetValue((convId, userId), out var r) ? r : null);

        public Task UpsertThreadReadAsync(ChatThreadRead read, CancellationToken ct)
        {
            var existing = Reads.TryGetValue((read.ConversationId, read.UserId), out var r) ? r : null;
            if (existing is null)
            {
                Reads[(read.ConversationId, read.UserId)] = read;
            }
            else
            {
                existing.GetType().GetProperty("LastReadMessageId")?.SetValue(existing, read.LastReadMessageId);
                existing.GetType().GetProperty("LastReadAt")?.SetValue(existing, read.LastReadAt);
            }
            return Task.CompletedTask;
        }

        public Task<int> CountUnreadAsync(Guid convId, Guid viewer, DateTime? lastReadAt, CancellationToken ct)
        {
            var q = Messages.Where(m => m.ConversationId == convId && m.SenderUserId != viewer);
            if (lastReadAt.HasValue) q = q.Where(m => m.CreatedAt > lastReadAt.Value);
            return Task.FromResult(q.Count());
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryFriendService : IFriendService
    {
        public HashSet<(Guid, Guid)> Friends { get; } = new();
        public Task<bool> IsFriendAsync(Guid a, Guid b, CancellationToken ct)
        {
            var key = a.CompareTo(b) < 0 ? (a, b) : (b, a);
            return Task.FromResult(Friends.Contains(key));
        }
        public Task<FriendRequestView> SendRequestAsync(Guid s, Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task AcceptRequestAsync(Guid a, Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task RejectRequestAsync(Guid a, Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task CancelRequestAsync(Guid a, Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task UnfriendAsync(Guid a, Guid o, CancellationToken ct) { Friends.Remove((a.CompareTo(o) < 0 ? (a, o) : (o, a))); return Task.CompletedTask; }
        public Task<FriendPage> ListFriendsAsync(Guid u, string? c, int l, CancellationToken ct) => throw new NotImplementedException();
        public Task<FriendRequestStatusView> GetRelationStatusAsync(Guid v, Guid o, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<FriendRequestDetailedView>> ListPendingRequestsAsync(Guid u, string t, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class InMemoryBlockStore : IBlockStore
    {
        public HashSet<(Guid, Guid)> Blocks { get; } = new();
        public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct)
            => Task.FromResult(Blocks.Contains((a, b)) || Blocks.Contains((b, a)));
        public Task<bool> BlockExistsAsync(Guid b, Guid d, CancellationToken ct) => Task.FromResult(Blocks.Contains((b, d)));
        public Task AddBlockAsync(BlockedUser x, CancellationToken ct) { Blocks.Add((x.BlockerId, x.BlockedId)); return Task.CompletedTask; }
        public Task<bool> RemoveBlockAsync(Guid b, Guid d, CancellationToken ct) => Task.FromResult(Blocks.Remove((b, d)));
        public Task<(IReadOnlyList<Guid>, string?)> ListBlockedIdsAsync(Guid b, DateTime? c, Guid? i, int l, CancellationToken ct) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Enqueued { get; } = new();
        public void Enqueue(string type, object payload) => Enqueued.Add((type, payload));
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static ChatService CreateSvc(InMemoryChatStore store, InMemoryFriendService friendSvc, InMemoryBlockStore blocks, CapturingOutbox outbox, DateTimeOffset? now = null)
    {
        return new ChatService(store, friendSvc, blocks, outbox, new FixedClock(now ?? new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero)));
    }

    // =========== Permission tests ===========

    [Fact]
    public async Task Send_WithSelf_ThrowsValidation()
    {
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), new InMemoryBlockStore(), new CapturingOutbox());
        var act = () => svc.SendFriendMessageAsync(Alice, Alice, "hi", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.CannotSelfMessage);
    }

    [Fact]
    public async Task Send_WhenBlocked_ThrowsForbidden()
    {
        var blocks = new InMemoryBlockStore();
        blocks.Blocks.Add((Alice, Bob));
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), blocks, new CapturingOutbox());
        var act = () => svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.Blocked);
    }

    [Fact]
    public async Task Send_WhenBlockedByOtherDirection_ThrowsForbidden()
    {
        var blocks = new InMemoryBlockStore();
        blocks.Blocks.Add((Bob, Alice)); // Bob blocks Alice
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), blocks, new CapturingOutbox());
        var act = () => svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.Blocked);
    }

    [Fact]
    public async Task Send_WhenNotFriend_ThrowsForbidden()
    {
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), new InMemoryBlockStore(), new CapturingOutbox());
        var act = () => svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.NotFriend);
    }

    [Fact]
    public async Task Send_WhenEmptyText_ThrowsValidation()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var svc = CreateSvc(new InMemoryChatStore(), friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        var act = () => svc.SendFriendMessageAsync(Alice, Bob, "   ", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.EmptyText);
    }

    [Fact]
    public async Task Send_WhenTextTooLong_ThrowsValidation()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var svc = CreateSvc(new InMemoryChatStore(), friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        var longText = new string('x', 2001);
        var act = () => svc.SendFriendMessageAsync(Alice, Bob, longText, null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.TextTooLong);
    }

    [Fact]
    public async Task Send_WhenUnauthenticated_ThrowsUnauthorized()
    {
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), new InMemoryBlockStore(), new CapturingOutbox());
        var act = () => svc.SendFriendMessageAsync(Guid.Empty, Bob, "hi", null, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<UnauthorizedException>();
        ex.Which.Code.Should().Be(ErrorCodes.Identity.NotAuthenticated);
    }

    // =========== Idempotency tests ===========

    [Fact]
    public async Task Send_SameClientMessageId_SamePayload_ReturnsExisting()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var outbox = new CapturingOutbox();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), outbox);
        var clientId = Guid.NewGuid();
        var first = await svc.SendFriendMessageAsync(Alice, Bob, "hello", clientId, CancellationToken.None);
        outbox.Enqueued.Count.Should().Be(1);

        var second = await svc.SendFriendMessageAsync(Alice, Bob, "hello", clientId, CancellationToken.None);
        second.Id.Should().Be(first.Id);
        outbox.Enqueued.Count.Should().Be(1); // no new outbox event
        store.Messages.Count.Should().Be(1);
    }

    [Fact]
    public async Task Send_SameClientMessageId_DifferentPayload_ThrowsConflict()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var svc = CreateSvc(new InMemoryChatStore(), friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        var clientId = Guid.NewGuid();
        await svc.SendFriendMessageAsync(Alice, Bob, "hello", clientId, CancellationToken.None);
        var act = () => svc.SendFriendMessageAsync(Alice, Bob, "different", clientId, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.IdempotencyMismatch);
    }

    // =========== Conversation lifecycle ===========

    [Fact]
    public async Task Send_CreatesConversation_WithCanonicalPair()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        await svc.SendFriendMessageAsync(Alice, Bob, "a", null, CancellationToken.None);
        await svc.SendFriendMessageAsync(Bob, Alice, "b", null, CancellationToken.None);

        store.Conversations.Count.Should().Be(1);
        var conv = store.Conversations.Values.Single();
        var (low, high) = FriendConversation.CanonicalPair(Alice, Bob);
        conv.UserLowId.Should().Be(low);
        conv.UserHighId.Should().Be(high);
    }

    [Fact]
    public async Task Unfriend_PreservesConversation_AndMessages()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var convId = store.Conversations.Values.Single().Id;
        store.Messages.Count.Should().Be(1);

        await friendSvc.UnfriendAsync(Alice, Bob, CancellationToken.None);

        store.Conversations.Count.Should().Be(1);
        store.Messages.Count.Should().Be(1);

        // Sau unfriend vẫn list được messages
        var page = await svc.ListMessagesAsync(Alice, convId, null, 10, CancellationToken.None);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Block_PreventsSend_ButPreservesHistory()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var blocks = new InMemoryBlockStore();
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, blocks, new CapturingOutbox());

        await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var convId = store.Conversations.Values.Single().Id;

        blocks.Blocks.Add((Bob, Alice));
        var sendAfter = () => svc.SendFriendMessageAsync(Alice, Bob, "no", null, CancellationToken.None);
        await sendAfter.Should().ThrowAsync<ForbiddenException>();

        var page = await svc.ListMessagesAsync(Alice, convId, null, 10, CancellationToken.None);
        page.Items.Should().HaveCount(1);
    }

    // =========== Read marker tests ===========

    [Fact]
    public async Task MarkRead_AdvancesMarker()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var clock = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox(), clock);

        var m1 = await svc.SendFriendMessageAsync(Alice, Bob, "first", null, CancellationToken.None);
        // Bob reply later so m2.CreatedAt > m1.CreatedAt
        var m2 = await svc.SendFriendMessageAsync(Bob, Alice, "second", null, CancellationToken.None);

        await svc.MarkReadAsync(Bob, m1.ConversationId!.Value, m2.Id, CancellationToken.None);
        store.Reads[(m1.ConversationId.Value, Bob)].LastReadMessageId.Should().Be(m2.Id);
    }

    [Fact]
    public async Task MarkRead_OldMessage_IsNoOp()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m1 = await svc.SendFriendMessageAsync(Alice, Bob, "first", null, CancellationToken.None);
        var m2 = await svc.SendFriendMessageAsync(Bob, Alice, "second", null, CancellationToken.None);

        // Bob đánh dấu đã đọc m2 trước
        await svc.MarkReadAsync(Bob, m1.ConversationId!.Value, m2.Id, CancellationToken.None);
        var initial = store.Reads[(m1.ConversationId.Value, Bob)].LastReadMessageId;

        // Sau đó Bob cố đánh dấu lại với m1 (cũ hơn) → no-op
        await svc.MarkReadAsync(Bob, m1.ConversationId.Value, m1.Id, CancellationToken.None);
        store.Reads[(m1.ConversationId.Value, Bob)].LastReadMessageId.Should().Be(initial);
    }

    [Fact]
    public async Task MarkRead_ByNonParticipant_ThrowsNotFound()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m = await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var act = () => svc.MarkReadAsync(Carol, m.ConversationId!.Value, m.Id, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MarkRead_WithMessageNotInConversation_ThrowsValidation()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        friendSvc.Friends.Add(Alice.CompareTo(Carol) < 0 ? (Alice, Carol) : (Carol, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var mAB = await svc.SendFriendMessageAsync(Alice, Bob, "ab", null, CancellationToken.None);
        var mAC = await svc.SendFriendMessageAsync(Alice, Carol, "ac", null, CancellationToken.None);

        var act = () => svc.MarkReadAsync(Bob, mAB.ConversationId!.Value, mAC.Id, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.MessageNotFound);
    }

    [Fact]
    public async Task MarkRead_WithUnknownMessage_ThrowsValidation()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m = await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var unknownId = Guid.NewGuid();
        var act = () => svc.MarkReadAsync(Bob, m.ConversationId!.Value, unknownId, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    // =========== Unread count ===========

    [Fact]
    public async Task UnreadCount_ExcludesOwnMessages()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        await svc.SendFriendMessageAsync(Alice, Bob, "from alice 1", null, CancellationToken.None);
        await svc.SendFriendMessageAsync(Alice, Bob, "from alice 2", null, CancellationToken.None);
        await svc.SendFriendMessageAsync(Bob, Alice, "from bob 1", null, CancellationToken.None);

        var page = await svc.ListConversationsAsync(Alice, null, 10, CancellationToken.None);
        page.Items.Single().UnreadCount.Should().Be(1); // chỉ 1 từ Bob

        var bobView = await svc.ListConversationsAsync(Bob, null, 10, CancellationToken.None);
        bobView.Items.Single().UnreadCount.Should().Be(2); // 2 từ Alice
    }

    [Fact]
    public async Task UnreadCount_DecreasesAfterMarkRead()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m1 = await svc.SendFriendMessageAsync(Alice, Bob, "first", null, CancellationToken.None);
        var m2 = await svc.SendFriendMessageAsync(Alice, Bob, "second", null, CancellationToken.None);

        var before = await svc.ListConversationsAsync(Bob, null, 10, CancellationToken.None);
        before.Items.Single().UnreadCount.Should().Be(2);

        await svc.MarkReadAsync(Bob, m1.ConversationId!.Value, m2.Id, CancellationToken.None);

        var after = await svc.ListConversationsAsync(Bob, null, 10, CancellationToken.None);
        after.Items.Single().UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task UnreadCount_IncludesMessagesArrivingAfterMarkRead()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m1 = await svc.SendFriendMessageAsync(Alice, Bob, "first", null, CancellationToken.None);
        await svc.MarkReadAsync(Bob, m1.ConversationId!.Value, m1.Id, CancellationToken.None);

        var m2 = await svc.SendFriendMessageAsync(Alice, Bob, "second", null, CancellationToken.None);

        var page = await svc.ListConversationsAsync(Bob, null, 10, CancellationToken.None);
        page.Items.Single().UnreadCount.Should().Be(1); // m2 mới đến sau marker
    }

    // =========== ListMessages ownership ===========

    [Fact]
    public async Task ListMessages_ByNonParticipant_ThrowsNotFound()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m = await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var act = () => svc.ListMessagesAsync(Carol, m.ConversationId!.Value, null, 10, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NotFoundException>();
        ex.Which.Code.Should().Be(ErrorCodes.Chat.ConversationNotFound);
    }

    [Fact]
    public async Task ListMessages_UnknownConversation_ThrowsNotFound()
    {
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), new InMemoryBlockStore(), new CapturingOutbox());
        var act = () => svc.ListMessagesAsync(Alice, Guid.NewGuid(), null, 10, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // =========== Pagination ===========

    [Fact]
    public async Task ListMessages_LimitClamped_DefaultsTo30()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var first = await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        for (var i = 0; i < 35; i++)
            await svc.SendFriendMessageAsync(Alice, Bob, $"m{i}", null, CancellationToken.None);

        var page = await svc.ListMessagesAsync(Alice, first.ConversationId!.Value, null, 10, CancellationToken.None);
        page.Items.Count.Should().Be(10);
        page.NextCursor.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ListConversations_LimitOver100_ClampedTo100()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        await svc.SendFriendMessageAsync(Alice, Bob, "x", null, CancellationToken.None);
        var page = await svc.ListConversationsAsync(Alice, null, 500, CancellationToken.None);
        page.Items.Count.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public async Task ListConversations_Empty_ReturnsEmptyPage()
    {
        var svc = CreateSvc(new InMemoryChatStore(), new InMemoryFriendService(), new InMemoryBlockStore(), new CapturingOutbox());
        var page = await svc.ListConversationsAsync(Alice, null, 30, CancellationToken.None);
        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    // =========== Race transaction ===========

    [Fact]
    public async Task Send_EnqueuesOutbox_InSameTransaction()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var outbox = new CapturingOutbox();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), outbox);

        await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);

        outbox.Enqueued.Should().HaveCount(1);
        outbox.Enqueued[0].Type.Should().Be("chat.message.sent");
    }

    [Fact]
    public async Task ToggleReaction_AddsChangesAndRemovesViewerReaction()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        var message = await svc.SendFriendMessageAsync(Alice, Bob, "hello", null, CancellationToken.None);

        var added = await svc.ToggleReactionAsync(Bob, message.Id, "❤️", CancellationToken.None);
        added.Should().ContainSingle(x => x.Reaction == "❤️" && x.Count == 1 && x.ReactedByViewer);

        var changed = await svc.ToggleReactionAsync(Bob, message.Id, "👍", CancellationToken.None);
        changed.Should().ContainSingle(x => x.Reaction == "👍" && x.Count == 1 && x.ReactedByViewer);

        var removed = await svc.ToggleReactionAsync(Bob, message.Id, "👍", CancellationToken.None);
        removed.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleReaction_ByNonParticipant_HidesMessage()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        var message = await svc.SendFriendMessageAsync(Alice, Bob, "hello", null, CancellationToken.None);

        var act = () => svc.ToggleReactionAsync(Carol, message.Id, "❤️", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ToggleReaction_WhenBlocked_RejectsNewInteraction()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var blocks = new InMemoryBlockStore();
        var svc = CreateSvc(store, friendSvc, blocks, new CapturingOutbox());
        var message = await svc.SendFriendMessageAsync(Alice, Bob, "hello", null, CancellationToken.None);
        blocks.Blocks.Add((Alice, Bob));

        var act = () => svc.ToggleReactionAsync(Bob, message.Id, "❤️", CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // =========== GetConversation ===========

    [Fact]
    public async Task GetConversation_ByParticipant_ReturnsDto()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        var m = await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var conv = await svc.GetConversationAsync(Alice, m.ConversationId!.Value, CancellationToken.None);
        conv.Peer.UserId.Should().Be(Bob);
        conv.LastMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task GetConversation_ByNonParticipant_ThrowsNotFound()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());
        var m = await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        var act = () => svc.GetConversationAsync(Carol, m.ConversationId!.Value, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // =========== Conversation lookup by pair ===========

    [Fact]
    public async Task FindConversationForPair_ReverseOrder_FindsExisting()
    {
        var friendSvc = new InMemoryFriendService();
        friendSvc.Friends.Add(Alice.CompareTo(Bob) < 0 ? (Alice, Bob) : (Bob, Alice));
        var store = new InMemoryChatStore();
        var svc = CreateSvc(store, friendSvc, new InMemoryBlockStore(), new CapturingOutbox());

        await svc.SendFriendMessageAsync(Alice, Bob, "hi", null, CancellationToken.None);
        // Tìm lại với order ngược → vẫn ra cùng conversation
        var found = await store.FindConversationForPairAsync(Bob, Alice, CancellationToken.None);
        found.Should().NotBeNull();
        var (low, high) = FriendConversation.CanonicalPair(Alice, Bob);
        found!.UserLowId.Should().Be(low);
        found.UserHighId.Should().Be(high);
    }
}
