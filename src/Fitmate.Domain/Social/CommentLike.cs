using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class CommentLike : AuditableEntity
{
    public Guid CommentId { get; private set; }
    public Guid UserId { get; private set; }

    private CommentLike() { }

    public static CommentLike Create(Guid commentId, Guid userId) => new()
    {
        CommentId = commentId,
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
