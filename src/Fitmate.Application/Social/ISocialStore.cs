using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

public interface ISocialStore
{
    Task<(IReadOnlyList<SocialPostRow> Items, string? NextCursor)> ListPostsAsync(
        Guid? viewerId, FeedScope scope, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct);

    Task<SocialPostRow?> GetPostByIdAsync(Guid postId, CancellationToken ct);
    Task<SocialPostRow?> GetPostWithAuthorAsync(Guid postId, CancellationToken ct);
    Task AddPostAsync(SocialPostRow row, CancellationToken ct);

    Task<bool> RemovePostAsync(Guid postId, CancellationToken ct);

    Task<bool> PostExistsAsync(Guid postId, CancellationToken ct);
    Task AddCommentAsync(SocialCommentRow row, CancellationToken ct);

    Task<bool> HasReactionAsync(Guid postId, Guid userId, CancellationToken ct);
    Task AddReactionAsync(SocialReactionRow row, CancellationToken ct);
    Task<int> ChangePostReactionsAsync(Guid postId, int delta, CancellationToken ct);
    Task<int> ChangePostCommentsAsync(Guid postId, int delta, CancellationToken ct);

    Task<bool> TargetExistsAsync(ReportTargetKind kind, Guid targetId, CancellationToken ct);
    Task AddReportAsync(Guid reporterId, ReportTargetKind kind, Guid targetId, string reason, string? details, CancellationToken ct);

    Task<SocialAuthorRow?> GetAuthorAsync(Guid userId, CancellationToken ct);
}

public sealed record SocialPostRow(
    Guid Id,
    Guid AuthorId,
    string Content,
    string? ImageStorageKey,
    string? Link,
    IReadOnlyList<string> Tags,
    PostVisibility Visibility,
    bool IsKnowledge,
    int ReactionsCount,
    int CommentsCount,
    DateTime CreatedAt);

public sealed record SocialCommentRow(
    Guid Id,
    Guid PostId,
    Guid AuthorId,
    string Text,
    DateTime CreatedAt);

public sealed record SocialReactionRow(
    Guid Id,
    Guid PostId,
    Guid UserId,
    ReactionType Type,
    DateTime CreatedAt);

public sealed record SocialAuthorRow(Guid Id, string DisplayName, string? AvatarUrl);
