using Fitmate.Domain.Chat;

namespace Fitmate.Application.Chat;

/// <summary>
/// BE-191: friend chat use cases. Service đảm bảo:
/// - friend-only (không AI thread)
/// - block 2 chiều + self-message check
/// - race-safe send (transaction + canonical pair insert)
/// - idempotent retry (client_message_id)
/// - read marker monotonic (multi-device không lùi)
/// - history giữ nguyên sau unfriend/block
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Gửi 1 message cho friend. Trả message DTO. Idempotent qua <paramref name="clientMessageId"/>:
    /// cùng (conversation, clientMessageId) + cùng payload → trả row cũ; khác payload → ConflictException.
    /// </summary>
    Task<ChatMessageDto> SendFriendMessageAsync(
        Guid senderId, Guid peerUserId, string text, Guid? clientMessageId, CancellationToken ct);

    /// <summary>
    /// List conversations của caller, kèm peer summary + last message + unread count. Cursor pagination.
    /// </summary>
    Task<ConversationPage> ListConversationsAsync(Guid viewerId, string? cursor, int limit, CancellationToken ct);

    /// <summary>
    /// List messages trong 1 conversation mà caller là participant. Cursor (CreatedAt desc, Id desc).
    /// </summary>
    Task<MessagesPage> ListMessagesAsync(Guid viewerId, Guid conversationId, string? cursor, int limit, CancellationToken ct);

    /// <summary>
    /// Marker read cho conversation. Monotonic: chỉ advance nếu candidate message mới hơn marker hiện tại.
    /// </summary>
    Task MarkReadAsync(Guid viewerId, Guid conversationId, Guid lastReadMessageId, CancellationToken ct);

    /// <summary>Single conversation info (cho navigation từ notification/deep link).</summary>
    Task<ConversationDto> GetConversationAsync(Guid viewerId, Guid conversationId, CancellationToken ct);
}

// ==================== DTOs ====================

public sealed record ChatMessageDto(
    Guid Id,
    Guid? ConversationId,
    Guid SenderUserId,
    string Sender,
    string Text,
    DateTime CreatedAt,
    Guid? ClientMessageId);

public sealed record FriendSummaryDto(
    Guid UserId,
    string? DisplayName,
    string? AvatarUrl);

public sealed record ConversationDto(
    Guid ConversationId,
    FriendSummaryDto Peer,
    ChatMessageDto? LastMessage,
    int UnreadCount,
    DateTime LastMessageAt);

public sealed record ConversationPage(IReadOnlyList<ConversationDto> Items, string? NextCursor);

public sealed record MessagesPage(IReadOnlyList<ChatMessageDto> Items, string? NextCursor);

// ==================== Request DTOs ====================

public sealed record SendFriendMessageRequest(
    Guid PeerUserId,
    string Text,
    Guid? ClientMessageId);
