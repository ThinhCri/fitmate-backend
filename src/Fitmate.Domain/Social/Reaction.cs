using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class Reaction : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid UserId { get; private set; }
    public ReactionType Type { get; private set; }

    private Reaction() { }
    public static Reaction Create(Guid postId, Guid userId, ReactionType type) => new()
    {
        Id = Guid.NewGuid(),
        PostId = postId,
        UserId = userId,
        Type = type,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
