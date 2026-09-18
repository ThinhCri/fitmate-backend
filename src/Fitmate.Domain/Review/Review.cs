using Fitmate.Domain.Common;

namespace Fitmate.Domain.Review;

public class Review : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid FromUserId { get; private set; }
    public Guid PtUserId { get; private set; }
    public int Rating { get; private set; }
    public string Text { get; private set; } = null!;

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
}
