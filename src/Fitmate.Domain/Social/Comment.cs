using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class Comment : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Text { get; private set; } = null!;

    private Comment() { }
    public static Comment Create(Guid postId, Guid authorId, string text) => new()
    {
        Id = Guid.NewGuid(),
        PostId = postId,
        AuthorId = authorId,
        Text = text,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>BE-211: comment soft-delete khi admin resolve report với DeleteContent.</summary>
    public new void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        Touch();
    }
}
