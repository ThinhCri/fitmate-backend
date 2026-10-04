using Fitmate.Domain.Common;

namespace Fitmate.Domain.Chat;

public class ChatThread : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public string? Title { get; private set; }
    public DateTime LastMessageAt { get; private set; }

    private ChatThread() { }
    public static ChatThread CreateAi(Guid memberId, string? title) => new()
    {
        Id = Guid.NewGuid(),
        MemberId = memberId,
        Title = title,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        LastMessageAt = DateTime.UtcNow
    };

    public void TouchLastMessage()
    {
        LastMessageAt = DateTime.UtcNow;
        Touch();
    }

    public void Rename(string title)
    {
        Title = title;
        Touch();
    }
}
