using Fitmate.Application.Moderation;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

public interface ISocialStore
{
    Task<(IReadOnlyList<SocialPostRow> Items, string? NextCursor)> ListPostsAsync(
        Guid? viewerId, FeedScope scope, DateTime? cursorCreatedAt, Guid? cursorId, int limit, string? query, CancellationToken ct);
    Task<IReadOnlyList<SocialPostRow>> ListBookmarkedPostsAsync(
        Guid viewerId, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct);

    Task<SocialPostRow?> GetPostByIdAsync(Guid postId, CancellationToken ct);
    Task<SocialPostRow?> GetPostWithAuthorAsync(Guid postId, CancellationToken ct);
    Task AddPostAsync(SocialPostRow row, CancellationToken ct);
    Task<SocialPostRow?> UpdatePostVisibilityAsync(Guid postId, PostVisibility visibility, CancellationToken ct);
    Task<SocialPostRow?> UpdatePostAsync(Guid postId, string content, IReadOnlyList<string> imageStorageKeys, IReadOnlyList<string> tags, bool isKnowledge, CancellationToken ct);

    Task<bool> RemovePostAsync(Guid postId, CancellationToken ct);

    Task<bool> PostExistsAsync(Guid postId, CancellationToken ct);
    Task AddCommentAsync(SocialCommentRow row, CancellationToken ct);
    Task<(IReadOnlyList<SocialCommentRow> Items, string? NextCursor)> ListCommentsAsync(
        Guid postId, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct);

    Task<bool> HasReactionAsync(Guid postId, Guid userId, CancellationToken ct);
    Task AddReactionAsync(SocialReactionRow row, CancellationToken ct);
    Task RemoveReactionAsync(Guid postId, Guid userId, CancellationToken ct);
    Task<int> ChangePostReactionsAsync(Guid postId, int delta, CancellationToken ct);
    Task<int> ChangePostCommentsAsync(Guid postId, int delta, CancellationToken ct);

    Task<bool> TargetExistsAsync(ReportTargetType kind, Guid targetId, CancellationToken ct);
    Task<Guid> AddReportAsync(Guid reporterId, ReportTargetType kind, Guid targetId, string reason, string? details, string? idempotencyKey, CancellationToken ct);

    /// <summary>BE-211: get comment by id.</summary>
    Task<SocialCommentRow?> GetCommentAsync(Guid commentId, CancellationToken ct);
    Task<(bool Saved, bool Changed)> ToggleBookmarkAsync(Guid postId, Guid userId, CancellationToken ct);
    Task<bool> IsBookmarkedAsync(Guid postId, Guid userId, CancellationToken ct);
    Task<(bool Liked, int LikesCount)> ToggleCommentLikeAsync(Guid commentId, Guid userId, CancellationToken ct);
    Task<(int LikesCount, bool LikedByViewer)> GetCommentLikeStateAsync(Guid commentId, Guid? viewerId, CancellationToken ct);

    /// <summary>BE-211: commit transaction cho 1 hoặc nhiều operation cùng DB context.</summary>
    Task SaveChangesAsync(CancellationToken ct);

    /// <summary>BE-211: spam check — tìm Open report trùng từ cùng reporter (DEC-028).</summary>
    Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct);

    /// <summary>BE-211: idempotency check — tìm report đã tạo với cùng (reporterId, idempotencyKey).</summary>
    Task<ReportRow?> FindOpenReportByKeyAsync(Guid reporterId, string idempotencyKey, CancellationToken ct);

    Task<SocialAuthorRow?> GetAuthorAsync(Guid userId, CancellationToken ct);
}

public sealed record SocialPostRow(
    Guid Id,
    Guid AuthorId,
    string Content,
    string? ImageStorageKey,
    IReadOnlyList<string> ImageStorageKeys,
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
    DateTime CreatedAt,
    Guid? ParentCommentId = null);

public sealed record SocialReactionRow(
    Guid Id,
    Guid PostId,
    Guid UserId,
    ReactionType Type,
    DateTime CreatedAt);

public sealed record SocialAuthorRow(Guid Id, string DisplayName, string? AvatarUrl);
