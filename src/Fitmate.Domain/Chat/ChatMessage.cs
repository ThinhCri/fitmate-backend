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
    public List<string> ImageStorageKeys { get; private set; } = new();
    public List<ChatMessageReaction> Reactions { get; private set; } = new();
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

    public static ChatMessage CreatePeer(Guid conversationId, Guid senderId, string text, Guid? clientMessageId = null,
        IReadOnlyList<string>? imageStorageKeys = null) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        Sender = ChatMessageSender.Peer,
        SenderUserId = senderId,
        Text = text,
        ImageStorageKeys = imageStorageKeys?.Distinct(StringComparer.Ordinal).ToList() ?? new(),
        ClientMessageId = clientMessageId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}

public class ChatMessageReaction : AuditableEntity
{
    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public string Reaction { get; private set; } = null!;

    private ChatMessageReaction() { }
    public static ChatMessageReaction Create(Guid messageId, Guid userId, string reaction) => new()
    {
        MessageId = messageId,
        UserId = userId,
        Reaction = reaction,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Change(string reaction)
    {
        Reaction = reaction;
        Touch();
    }
}

public enum ChatMessageSender
{
    User = 0,
    Ai = 1,
    Peer = 2
}
