using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

public enum FeedScope { Public, Friends }

public sealed record PostAuthor(Guid Id, string DisplayName, string? AvatarUrl);

public sealed record PostView(
    Guid Id,
    Guid AuthorId,
    PostAuthor Author,
    string Content,
    string? ImageUrl,
    string? Link,
    IReadOnlyList<string> Tags,
    string Visibility,
    bool IsKnowledge,
    int ReactionsCount,
    int CommentsCount,
    DateTime CreatedAt);

public sealed record PostPage(IReadOnlyList<PostView> Items, string? NextCursor);

public sealed record PostInput(
    string Content,
    string? ImageStorageKey,
    string? Link,
    IReadOnlyList<string> Tags,
    PostVisibility Visibility,
    bool IsKnowledge);

public sealed record CommentView(
    Guid Id,
    Guid PostId,
    Guid AuthorId,
    PostAuthor Author,
    string Text,
    DateTime CreatedAt);

public enum ReportTargetKind { Post, Comment, User }

public sealed record ReportInputDto(
    ReportTargetKind TargetType,
    Guid TargetId,
    string Reason,
    string? Details);

public interface ISocialService
{
    Task<PostPage> ListPostsAsync(Guid? viewerId, FeedScope scope, string? cursor, int limit, CancellationToken ct);
    Task<PostView> GetPostAsync(Guid viewerId, Guid postId, CancellationToken ct);
    Task<PostView> CreatePostAsync(Guid authorId, PostInput input, CancellationToken ct);
    Task DeletePostAsync(Guid actorId, bool isAdmin, Guid postId, CancellationToken ct);
    Task<CommentView> AddCommentAsync(Guid authorId, Guid postId, string text, CancellationToken ct);
    Task ToggleReactionAsync(Guid userId, Guid postId, ReactionType type, CancellationToken ct);
    Task<Guid> CreateReportAsync(Guid reporterId, ReportInputDto input, CancellationToken ct);
}
