using System.Text.Json.Serialization;
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

    Task<ChatMessageDto> SendFriendMessageWithImagesAsync(
        Guid senderId, Guid peerUserId, string text, Guid? clientMessageId,
        IReadOnlyList<string> imageStorageKeys, CancellationToken ct)
        => SendFriendMessageAsync(senderId, peerUserId, text, clientMessageId, ct);

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
    Task<IReadOnlyList<ChatReactionDto>> ToggleReactionAsync(Guid viewerId, Guid messageId, string reaction, CancellationToken ct);
}

// ==================== DTOs ====================

public sealed record ChatMessageDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("conversation_id")] Guid? ConversationId,
    [property: JsonPropertyName("sender_user_id")] Guid SenderUserId,
    [property: JsonPropertyName("sender")] string Sender,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("client_message_id")] Guid? ClientMessageId,
    [property: JsonPropertyName("image_urls")] IReadOnlyList<string>? ImageUrls = null,
    [property: JsonPropertyName("image_storage_keys")] IReadOnlyList<string>? ImageStorageKeys = null,
    [property: JsonPropertyName("reactions")] IReadOnlyList<ChatReactionDto>? Reactions = null);

public sealed record ChatReactionDto(
    [property: JsonPropertyName("reaction")] string Reaction,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("reacted_by_viewer")] bool ReactedByViewer);

public sealed record FriendSummaryDto(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

public sealed record ConversationDto(
    [property: JsonPropertyName("conversation_id")] Guid ConversationId,
    [property: JsonPropertyName("peer")] FriendSummaryDto Peer,
    [property: JsonPropertyName("last_message")] ChatMessageDto? LastMessage,
    [property: JsonPropertyName("unread_count")] int UnreadCount,
    [property: JsonPropertyName("last_message_at")] DateTime LastMessageAt);

public sealed record ConversationPage(
    [property: JsonPropertyName("items")] IReadOnlyList<ConversationDto> Items,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

public sealed record MessagesPage(
    [property: JsonPropertyName("items")] IReadOnlyList<ChatMessageDto> Items,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

// ==================== Request DTOs ====================

public sealed record SendFriendMessageRequest(
    [property: JsonPropertyName("peer_user_id")] Guid PeerUserId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("client_message_id")] Guid? ClientMessageId,
    [property: JsonPropertyName("image_storage_keys")] IReadOnlyList<string>? ImageStorageKeys = null);
