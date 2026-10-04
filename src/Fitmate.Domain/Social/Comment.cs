using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class Comment : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid AuthorId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public string Text { get; private set; } = null!;

    private Comment() { }
    public static Comment Create(Guid postId, Guid authorId, string text, Guid? parentCommentId = null,
        Guid? id = null, DateTime? createdAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        PostId = postId,
        AuthorId = authorId,
        ParentCommentId = parentCommentId,
        Text = text,
        CreatedAt = createdAt ?? DateTime.UtcNow,
        UpdatedAt = createdAt ?? DateTime.UtcNow
    };

    /// <summary>BE-211: comment soft-delete khi admin resolve report với DeleteContent.</summary>
    public new void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        Touch();
    }
}
