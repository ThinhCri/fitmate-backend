using Fitmate.Domain.Chat;
using Fitmate.Domain.Common;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;

namespace Fitmate.Application.Chat;

public sealed class ChatService : IChatService
{
    private readonly IChatStore _store;
    private readonly IFriendService _friendService;
    private readonly IBlockStore _blockStore;
    private readonly IOutboxDispatcher _outbox;
    private readonly TimeProvider _clock;

    private const int MaxTextLength = 2000;
    private const int DefaultListLimit = 30;
    private const int MaxListLimit = 100;

    public ChatService(
        IChatStore store,
        IFriendService friendService,
        IBlockStore blockStore,
        IOutboxDispatcher outbox,
        TimeProvider clock)
    {
        _store = store;
        _friendService = friendService;
        _blockStore = blockStore;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<ChatMessageDto> SendFriendMessageAsync(
        Guid senderId, Guid peerUserId, string text, Guid? clientMessageId, CancellationToken ct)
    {
        EnsureAuthenticated(senderId);

        if (senderId == peerUserId)
            throw new ValidationException(ErrorCodes.Chat.CannotSelfMessage,
                "Cannot send message to yourself.",
                new Dictionary<string, string[]> { ["peerUserId"] = new[] { "Cannot message self." } });

        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException(ErrorCodes.Chat.EmptyText,
                "Message text is required.",
                new Dictionary<string, string[]> { ["text"] = new[] { "Text cannot be empty." } });

        if (text.Length > MaxTextLength)
            throw new ValidationException(ErrorCodes.Chat.TextTooLong,
                $"Message text exceeds {MaxTextLength} chars.",
                new Dictionary<string, string[]> { ["text"] = new[] { $"Text max length is {MaxTextLength}." } });

        if (await _blockStore.IsBlockedEitherWayAsync(senderId, peerUserId, ct))
            throw new ForbiddenException(ErrorCodes.Chat.Blocked, "Cannot send message: blocked.");

        if (!await _friendService.IsFriendAsync(senderId, peerUserId, ct))
            throw new ForbiddenException(ErrorCodes.Chat.NotFriend, "Only friends can chat.");

        var conversation = await _store.GetOrCreateConversationAsync(senderId, peerUserId, ct);

        if (clientMessageId.HasValue)
        {
            var existing = await _store.FindMessageByClientMessageIdAsync(conversation.Id, clientMessageId.Value, ct);
            if (existing is not null)
            {
                if (!string.Equals(existing.Text, text, StringComparison.Ordinal))
                    throw new ConflictException(ErrorCodes.Chat.IdempotencyMismatch,
                        "Same client_message_id with different payload.");
                return ToDto(existing);
            }
        }

        var message = ChatMessage.CreatePeer(conversation.Id, senderId, text, clientMessageId);
        await _store.AddMessageAsync(message, ct);

        conversation.TouchLastMessage();
        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("chat.message.sent", new
        {
            message_id = message.Id,
            conversation_id = conversation.Id,
            sender_id = senderId,
            peer_id = peerUserId,
            source_event_id = message.Id,
            source_type = "chat_message_sent"
        });

        return ToDto(message);
    }

    public async Task<ConversationPage> ListConversationsAsync(Guid viewerId, string? cursor, int limit, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);
        var (at, convId) = DecodeCursor(cursor);
        var effectiveLimit = ClampLimit(limit);

        var rows = await _store.ListConversationsAsync(viewerId, at, convId, effectiveLimit + 1, ct);
        var hasMore = rows.Count > effectiveLimit;
        var page = hasMore ? rows.Take(effectiveLimit).ToList() : rows.ToList();

        var items = page.Select(r => new ConversationDto(
            r.ConversationId,
            new FriendSummaryDto(r.PeerUserId, null, null),
            r.LastMessage is null ? null : ToDto(r.LastMessage),
            r.UnreadCount,
            r.LastMessageAt)).ToList();

        string? next = null;
        if (hasMore)
        {
            var last = page[^1];
            next = EncodeCursor(last.LastMessageAt, last.ConversationId);
        }
        return new ConversationPage(items, next);
    }

    public async Task<MessagesPage> ListMessagesAsync(Guid viewerId, Guid conversationId, string? cursor, int limit, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);
        var (createdAt, msgId) = DecodeCursor(cursor);
        var effectiveLimit = ClampLimit(limit);

        var conv = await _store.FindConversationAsync(conversationId, ct)
                   ?? throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Conversation not found.");

        if (!conv.IsParticipant(viewerId))
            throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Conversation not found.");

        var rows = await _store.ListMessagesAsync(conversationId, createdAt, msgId, effectiveLimit + 1, ct);
        var hasMore = rows.Count > effectiveLimit;
        var page = hasMore ? rows.Take(effectiveLimit).ToList() : rows.ToList();

        var items = page.Select(ToDto).ToList();
        string? next = null;
        if (hasMore)
        {
            var last = page[^1];
            next = EncodeCursor(last.CreatedAt, last.Id);
        }
        return new MessagesPage(items, next);
    }

    public async Task MarkReadAsync(Guid viewerId, Guid conversationId, Guid lastReadMessageId, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);

        var conv = await _store.FindConversationAsync(conversationId, ct)
                   ?? throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Conversation not found.");

        if (!conv.IsParticipant(viewerId))
            throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Conversation not found.");

        var candidate = await _store.FindMessageAsync(lastReadMessageId, ct)
                        ?? throw new ValidationException(ErrorCodes.Chat.MessageNotFound,
                            "last_read_message_id not found.",
                            new Dictionary<string, string[]> { ["lastReadMessageId"] = new[] { "Message not found." } });

        if (candidate.ConversationId != conversationId)
            throw new ValidationException(ErrorCodes.Chat.MessageNotFound,
                "Message does not belong to conversation.",
                new Dictionary<string, string[]> { ["lastReadMessageId"] = new[] { "Message not in conversation." } });

        var existing = await _store.GetThreadReadAsync(conversationId, viewerId, ct);
        var now = _clock.GetUtcNow().UtcDateTime;

        if (existing is null)
        {
            var row = ChatThreadRead.Create(conversationId, viewerId, lastReadMessageId, now);
            await _store.UpsertThreadReadAsync(row, ct);
            await _store.SaveChangesAsync(ct);
            return;
        }

        var currentRead = await _store.FindMessageAsync(existing.LastReadMessageId ?? Guid.Empty, ct);
        if (currentRead is not null && candidate.CreatedAt <= currentRead.CreatedAt)
        {
            // No-op: marker không lùi.
            return;
        }

        existing.TryAdvance(lastReadMessageId, now);
        await _store.UpsertThreadReadAsync(existing, ct);
        await _store.SaveChangesAsync(ct);
    }

    public async Task<ConversationDto> GetConversationAsync(Guid viewerId, Guid conversationId, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);
        var conv = await _store.FindConversationAsync(conversationId, ct)
                   ?? throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Conversation not found.");

        if (!conv.IsParticipant(viewerId))
            throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Conversation not found.");

        var peer = conv.OtherParticipant(viewerId);
        var last = await _store.GetLastMessageAsync(conversationId, ct);
        var read = await _store.GetThreadReadAsync(conversationId, viewerId, ct);
        var unread = await _store.CountUnreadAsync(conversationId, viewerId, read?.LastReadAt, ct);

        return new ConversationDto(
            conversationId,
            new FriendSummaryDto(peer, null, null),
            last is null ? null : ToDto(last),
            unread,
            conv.LastMessageAt);
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id,
        m.ConversationId,
        m.SenderUserId ?? Guid.Empty,
        m.Sender.ToString().ToLowerInvariant(),
        m.Text,
        m.CreatedAt,
        m.ClientMessageId);

    private static int ClampLimit(int limit)
    {
        if (limit <= 0) return DefaultListLimit;
        if (limit > MaxListLimit) return MaxListLimit;
        return limit;
    }

    private static (DateTime? at, Guid? id) DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            if (parts.Length != 2) return (null, null);
            if (!long.TryParse(parts[0], out var ticks)) return (null, null);
            if (!Guid.TryParse(parts[1], out var id)) return (null, null);
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string EncodeCursor(DateTime utc, Guid id)
    {
        var raw = $"{utc.Ticks}|{id}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    private static void EnsureAuthenticated(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new UnauthorizedException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
    }
}
