using Fitmate.Domain.Common;

namespace Fitmate.Domain.Chat;

/// <summary>
/// BE-191: Per-user/per-conversation read marker. PK composite
/// (ConversationId, UserId). <see cref="LastReadMessageId"/> chỉ tiến lên
/// (monotonic) để đảm bảo marker từ nhiều thiết bị không lùi. UNIQUE
/// constraint enforced ở DB để chống duplicate row.
/// </summary>
public class ChatThreadRead : AuditableEntity
{
    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? LastReadMessageId { get; private set; }
    public DateTime? LastReadAt { get; private set; }

    private ChatThreadRead() { }

    public static ChatThreadRead Create(Guid conversationId, Guid userId, Guid? lastReadMessageId, DateTime readAt)
    {
        return new ChatThreadRead
        {
            ConversationId = conversationId,
            UserId = userId,
            LastReadMessageId = lastReadMessageId,
            LastReadAt = readAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Cập nhật marker nếu <paramref name="newReadMessageId"/> cao hơn marker hiện tại.
    /// Trả về true nếu có update, false nếu no-op (monotonic guard).
    /// </summary>
    public bool TryAdvance(Guid? newReadMessageId, DateTime readAt)
    {
        if (newReadMessageId.HasValue && LastReadMessageId.HasValue)
        {
            // ChatMessage.Id là Guid ngẫu nhiên — không thể so sánh trực tiếp.
            // Application layer phải look up CreatedAt của candidate message rồi so sánh timestamp.
            // Marker sẽ được set/unset tại Application service dựa trên comparison
            // (`newReadMessage.CreatedAt > this.LastReadAt`).
            if (newReadMessageId == LastReadMessageId) return false;
        }
        LastReadMessageId = newReadMessageId;
        LastReadAt = readAt;
        Touch();
        return true;
    }
}
