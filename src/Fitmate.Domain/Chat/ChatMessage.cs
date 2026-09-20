using Fitmate.Domain.Common;

namespace Fitmate.Domain.Chat;

public class ChatMessage : AuditableEntity
{
    public Guid Id { get; private set; }

    /// <summary>AI thread (legacy). Null khi message thuộc friend conversation.</summary>
    public Guid? ThreadId { get; private set; }

    /// <summary>Friend conversation (BE-191). Null khi message thuộc AI thread.</summary>
    public Guid? ConversationId { get; private set; }

    public ChatMessageSender Sender { get; private set; }
    public Guid? SenderUserId { get; private set; }
    public string Text { get; private set; } = null!;
    public decimal? Confidence { get; private set; }

    /// <summary>
    /// Idempotency key do client sinh (UUID). Null nếu client không cung cấp.
    /// Unique trong scope 1 conversation (DB partial unique).
    /// </summary>
    public Guid? ClientMessageId { get; private set; }

    private ChatMessage() { }
    public static ChatMessage CreateUser(Guid threadId, Guid senderId, string text) => new()
    {
        Id = Guid.NewGuid(),
        ThreadId = threadId,
        Sender = ChatMessageSender.User,
        SenderUserId = senderId,
        Text = text,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public static ChatMessage CreateAi(Guid threadId, string text, decimal? confidence) => new()
    {
        Id = Guid.NewGuid(),
        ThreadId = threadId,
        Sender = ChatMessageSender.Ai,
        Text = text,
        Confidence = confidence,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public static ChatMessage CreatePeer(Guid conversationId, Guid senderId, string text, Guid? clientMessageId = null) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        Sender = ChatMessageSender.Peer,
        SenderUserId = senderId,
        Text = text,
        ClientMessageId = clientMessageId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

public enum ChatMessageSender
{
    User = 0,
    Ai = 1,
    Peer = 2
}
