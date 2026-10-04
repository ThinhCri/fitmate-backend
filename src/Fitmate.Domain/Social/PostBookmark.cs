using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class PostBookmark : AuditableEntity
{
    public Guid PostId { get; private set; }
    public Guid UserId { get; private set; }

    private PostBookmark() { }

    public static PostBookmark Create(Guid postId, Guid userId) => new()
    {
        PostId = postId,
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
