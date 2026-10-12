using Fitmate.Domain.Common;

namespace Fitmate.Domain.Review;

public class Review : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid FromUserId { get; private set; }
    public Guid PtUserId { get; private set; }
    public int Rating { get; private set; }
    public string Text { get; private set; } = null!;
    public bool IsHidden { get; private set; }
    public string? ModerationReason { get; private set; }
    public Guid? ModeratedByAdminId { get; private set; }
    public DateTime? ModeratedAtUtc { get; private set; }

    private Review() { }
    public static Review Create(Guid fromUserId, Guid ptUserId, int rating, string text) => new()
    {
        Id = Guid.NewGuid(),
        FromUserId = fromUserId,
        PtUserId = ptUserId,
        Rating = rating,
        Text = text,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void SetHidden(bool hidden, Guid adminId, string reason, DateTime nowUtc)
    {
        if (adminId == Guid.Empty || string.IsNullOrWhiteSpace(reason) ||
            nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Moderation requires an admin, reason and UTC time.");
        IsHidden = hidden;
        ModerationReason = reason.Trim();
        ModeratedByAdminId = adminId;
        ModeratedAtUtc = nowUtc;
        Touch();
    }
}
