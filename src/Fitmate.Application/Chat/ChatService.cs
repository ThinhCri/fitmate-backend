using Fitmate.Domain.Chat;
using Fitmate.Domain.Common;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Application.Storage;
using Fitmate.Application.Realtime;

namespace Fitmate.Application.Chat;

public sealed class ChatService : IChatService
{
    private readonly IChatStore _store;
    private readonly IFriendService _friendService;
    private readonly IBlockStore _blockStore;
    private readonly IOutboxDispatcher _outbox;
    private readonly TimeProvider _clock;
    private readonly ISignedUrlService? _signedUrl;
    private readonly IRealtimeNotifier? _realtime;

    private const int MaxTextLength = 2000;
    private const int DefaultListLimit = 30;
    private const int MaxListLimit = 100;
    private static readonly HashSet<string> AllowedReactions = new(StringComparer.Ordinal) { "❤️", "👍", "😂", "😮", "😢" };

    public ChatService(
        IChatStore store,
        IFriendService friendService,
        IBlockStore blockStore,
        IOutboxDispatcher outbox,
        TimeProvider clock,
        ISignedUrlService? signedUrl = null,
        IRealtimeNotifier? realtime = null)
    {
        _store = store;
        _friendService = friendService;
        _blockStore = blockStore;
        _outbox = outbox;
        _clock = clock;
        _signedUrl = signedUrl;
        _realtime = realtime;
    }

    public async Task<ChatMessageDto> SendFriendMessageAsync(
        Guid senderId, Guid peerUserId, string text, Guid? clientMessageId, CancellationToken ct)
        => await SendFriendMessageWithImagesAsync(senderId, peerUserId, text, clientMessageId, Array.Empty<string>(), ct);

    public async Task<ChatMessageDto> SendFriendMessageWithImagesAsync(
        Guid senderId, Guid peerUserId, string text, Guid? clientMessageId,
        IReadOnlyList<string> imageStorageKeys, CancellationToken ct)
    {
        EnsureAuthenticated(senderId);

        if (senderId == peerUserId)
            throw new ValidationException(ErrorCodes.Chat.CannotSelfMessage,
                "Cannot send message to yourself.",
                new Dictionary<string, string[]> { ["peerUserId"] = new[] { "Cannot message self." } });

        var normalizedText = text?.Trim() ?? string.Empty;
        var normalizedImages = imageStorageKeys.Distinct(StringComparer.Ordinal).ToList();
        if (string.IsNullOrWhiteSpace(normalizedText) && normalizedImages.Count == 0)
            throw new ValidationException(ErrorCodes.Chat.EmptyText,
                "Message text is required.",
                new Dictionary<string, string[]> { ["text"] = new[] { "Text cannot be empty." } });

        if (normalizedText.Length > MaxTextLength)
            throw new ValidationException(ErrorCodes.Chat.TextTooLong,
                $"Message text exceeds {MaxTextLength} chars.",
                new Dictionary<string, string[]> { ["text"] = new[] { $"Text max length is {MaxTextLength}." } });
        if (normalizedImages.Count > 5)
            throw new ValidationException(ErrorCodes.Validation.TooMany, "Mỗi tin nhắn được gửi tối đa 5 ảnh.",
                new Dictionary<string, string[]> { ["image_storage_keys"] = new[] { "max 5 images" } });
        var expectedPrefix = $"chat/{senderId:N}_";
        if (normalizedImages.Any(key => string.IsNullOrWhiteSpace(key) || !key.StartsWith(expectedPrefix, StringComparison.Ordinal)))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Ảnh phải do chính tài khoản hiện tại tải lên.",
                new Dictionary<string, string[]> { ["image_storage_keys"] = new[] { "invalid ownership" } });

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
                if (!string.Equals(existing.Text, normalizedText, StringComparison.Ordinal) ||
                    !existing.ImageStorageKeys.SequenceEqual(normalizedImages, StringComparer.Ordinal))
                    throw new ConflictException(ErrorCodes.Chat.IdempotencyMismatch,
                        "Same client_message_id with different payload.");
                return ToDto(existing, senderId);
            }
        }

        var message = ChatMessage.CreatePeer(conversation.Id, senderId, normalizedText, clientMessageId, normalizedImages);
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

        if (_realtime is not null)
            await _realtime.NotifyUsersAsync(new[] { senderId, peerUserId }, "chat.updated", new
            {
                conversation_id = conversation.Id,
                message_id = message.Id,
                reason = "message_sent"
            }, ct);

        return ToDto(message, senderId);
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
            r.LastMessage is null ? null : ToDto(r.LastMessage, viewerId),
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

        var items = page.Select(message => ToDto(message, viewerId)).ToList();
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
            last is null ? null : ToDto(last, viewerId),
            unread,
            conv.LastMessageAt);
    }

    public async Task<IReadOnlyList<ChatReactionDto>> ToggleReactionAsync(Guid viewerId, Guid messageId, string reaction, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);
        if (!AllowedReactions.Contains(reaction))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Cảm xúc không được hỗ trợ.",
                new Dictionary<string, string[]> { ["reaction"] = new[] { "allowed: ❤️ 👍 😂 😮 😢" } });
        var message = await _store.FindMessageAsync(messageId, ct)
            ?? throw new NotFoundException(ErrorCodes.Chat.MessageNotFound, "Không tìm thấy tin nhắn.");
        var conversation = message.ConversationId.HasValue
            ? await _store.FindConversationAsync(message.ConversationId.Value, ct)
            : null;
        if (conversation is null || !conversation.IsParticipant(viewerId))
            throw new NotFoundException(ErrorCodes.Chat.MessageNotFound, "Không tìm thấy tin nhắn.");
        var peerUserId = conversation.OtherParticipant(viewerId);
        if (await _blockStore.IsBlockedEitherWayAsync(viewerId, peerUserId, ct))
            throw new ForbiddenException(ErrorCodes.Chat.Blocked, "Không thể thả cảm xúc vì tương tác đã bị chặn.");
        if (!await _friendService.IsFriendAsync(viewerId, peerUserId, ct))
            throw new ForbiddenException(ErrorCodes.Chat.NotFriend, "Chỉ bạn bè mới có thể thả cảm xúc.");
        var existing = await _store.FindReactionAsync(messageId, viewerId, ct);
        if (existing is null) await _store.AddReactionAsync(ChatMessageReaction.Create(messageId, viewerId, reaction), ct);
        else if (existing.Reaction == reaction) _store.RemoveReaction(existing);
        else existing.Change(reaction);
        await _store.SaveChangesAsync(ct);
        message = await _store.FindMessageAsync(messageId, ct) ?? message;
        if (_realtime is not null)
            await _realtime.NotifyUsersAsync(new[] { conversation.UserLowId, conversation.UserHighId }, "chat.updated", new
            {
                conversation_id = conversation.Id,
                message_id = messageId,
                reason = "reaction_changed"
            }, ct);
        return MapReactions(message, viewerId);
    }

    private ChatMessageDto ToDto(ChatMessage m, Guid viewerId) => new(
        m.Id,
        m.ConversationId,
        m.SenderUserId ?? Guid.Empty,
        m.Sender.ToString().ToLowerInvariant(),
        m.Text,
        m.CreatedAt,
        m.ClientMessageId,
        _signedUrl is null
            ? new List<string>()
            : m.ImageStorageKeys.Select(key => _signedUrl.CreatePresignedReadUrl(key, TimeSpan.FromMinutes(15)).ToString()).ToList(),
        m.ImageStorageKeys,
        MapReactions(m, viewerId));

    private static IReadOnlyList<ChatReactionDto> MapReactions(ChatMessage message, Guid viewerId)
        => message.Reactions.GroupBy(item => item.Reaction)
            .Select(group => new ChatReactionDto(group.Key, group.Count(), group.Any(item => item.UserId == viewerId)))
            .OrderBy(item => Array.IndexOf(new[] { "❤️", "👍", "😂", "😮", "😢" }, item.Reaction))
            .ToList();

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
