using Fitmate.Domain.Common;

namespace Fitmate.Domain.Chat;

public class ChatMessage : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid ThreadId { get; private set; }
    public ChatMessageSender Sender { get; private set; }
    public Guid? SenderUserId { get; private set; }
    public string Text { get; private set; } = null!;
    public decimal? Confidence { get; private set; }

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

    public static ChatMessage CreatePeer(Guid threadId, Guid senderId, string text) => new()
    {
        Id = Guid.NewGuid(),
        ThreadId = threadId,
        Sender = ChatMessageSender.Peer,
        SenderUserId = senderId,
        Text = text,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
