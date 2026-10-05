using Fitmate.Domain.Chat;

namespace Fitmate.Application.Chat;

/// <summary>
/// BE-191: friend chat persistence. Store là abstraction thuần; EF Core ở
/// Infrastructure cung cấp implementation. Mỗi write operation nằm trong cùng
/// SaveChangesAsync với caller (transaction) để outbox + business row commit cùng lúc.
/// </summary>
public interface IChatStore
{
    /// <summary>
    /// Lấy hoặc tạo conversation canonical cho cặp user. Trả về conversation row đã tồn tại
    /// hoặc row mới. Caller chịu trách nhiệm check quyền (friend/block) trước khi gọi.
    /// </summary>
    Task<FriendConversation> GetOrCreateConversationAsync(Guid a, Guid b, CancellationToken ct);

    Task<FriendConversation?> FindConversationAsync(Guid conversationId, CancellationToken ct);

    Task<FriendConversation?> FindConversationForPairAsync(Guid a, Guid b, CancellationToken ct);

    Task AddMessageAsync(ChatMessage message, CancellationToken ct);

    /// <summary>
    /// Tìm message theo (conversationId, clientMessageId) để idempotency check.
    /// </summary>
    Task<ChatMessage?> FindMessageByClientMessageIdAsync(Guid conversationId, Guid clientMessageId, CancellationToken ct);

    /// <summary>
    /// Lấy last message của 1 conversation. Trả null nếu conversation rỗng.
    /// </summary>
    Task<ChatMessage?> GetLastMessageAsync(Guid conversationId, CancellationToken ct);

    /// <summary>
    /// List conversations của user, kèm last message + unread count. Cursor theo (LastMessageAt, Id).
    /// </summary>
    Task<IReadOnlyList<ConversationSummaryRow>> ListConversationsAsync(
        Guid userId, DateTime? cursorLastMessageAt, Guid? cursorConversationId, int limit, CancellationToken ct);

    /// <summary>
    /// List messages của conversation theo cursor (CreatedAt desc, Id desc). Cursor null = mới nhất.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(
        Guid conversationId, DateTime? cursorCreatedAt, Guid? cursorMessageId, int limit, CancellationToken ct);

    Task<ChatMessage?> FindMessageAsync(Guid messageId, CancellationToken ct);
    Task<ChatMessageReaction?> FindReactionAsync(Guid messageId, Guid userId, CancellationToken ct);
    Task AddReactionAsync(ChatMessageReaction reaction, CancellationToken ct);
    void RemoveReaction(ChatMessageReaction reaction);

    Task<ChatThreadRead?> GetThreadReadAsync(Guid conversationId, Guid userId, CancellationToken ct);

    Task UpsertThreadReadAsync(ChatThreadRead read, CancellationToken ct);

    Task<int> CountUnreadAsync(Guid conversationId, Guid viewerId, DateTime? lastReadAt, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// Row aggregate cho ListConversationsAsync — gộp conversation + last message + unread count
/// trong 1 query để tránh N+1.
/// </summary>
public sealed record ConversationSummaryRow(
    Guid ConversationId,
    Guid PeerUserId,
    DateTime LastMessageAt,
    ChatMessage? LastMessage,
    int UnreadCount);
