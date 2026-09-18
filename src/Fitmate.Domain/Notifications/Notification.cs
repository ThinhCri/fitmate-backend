using Fitmate.Domain.Common;

namespace Fitmate.Domain.Notifications;

public class Notification : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public string? PayloadJson { get; private set; }
    public bool Read { get; private set; }

    private Notification() { }
    public static Notification Create(Guid userId, NotificationType type, string title, string message, string? payloadJson) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = type,
        Title = title,
        Message = message,
        PayloadJson = payloadJson,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void MarkRead() { Read = true; Touch(); }
}
