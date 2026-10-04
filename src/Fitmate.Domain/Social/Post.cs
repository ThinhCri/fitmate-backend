using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class Post : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid AuthorId { get; private set; }
    public string Content { get; private set; } = null!;
    public string? ImageStorageKey { get; private set; }
    public List<string> ImageStorageKeys { get; private set; } = new();
    public string? Link { get; private set; }
    public List<string> Tags { get; private set; } = new();
    public PostVisibility Visibility { get; private set; }
    public bool IsKnowledge { get; private set; }
    public int ReactionsCount { get; private set; }
    public int CommentsCount { get; private set; }

    private Post() { }

    public static Post Create(Guid authorId, string content, string? imageStorageKey, string? link, List<string> tags, PostVisibility visibility, bool isKnowledge,
        IReadOnlyList<string>? imageStorageKeys = null, Guid? id = null, DateTime? createdAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AuthorId = authorId,
        Content = content,
        ImageStorageKey = imageStorageKey,
        ImageStorageKeys = imageStorageKeys?.ToList() ?? (imageStorageKey is null ? new() : new() { imageStorageKey }),
        Link = link,
        Tags = tags,
        Visibility = visibility,
        IsKnowledge = isKnowledge,
        CreatedAt = createdAt ?? DateTime.UtcNow,
        UpdatedAt = createdAt ?? DateTime.UtcNow
    };

    public void IncrementReactions(int delta) { ReactionsCount = Math.Max(0, ReactionsCount + delta); Touch(); }
    public void IncrementComments(int delta) { CommentsCount = Math.Max(0, CommentsCount + delta); Touch(); }
    public void ChangeVisibility(PostVisibility visibility) { Visibility = visibility; Touch(); }
    public void UpdateContent(string content) { Content = content; Touch(); }

    public new void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        Touch();
    }
}
