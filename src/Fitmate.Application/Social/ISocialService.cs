using System.Text.Json.Serialization;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

namespace Fitmate.Application.Social;

public enum FeedScope { Public, Friends }

public sealed record PostAuthor(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

public sealed record PostView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("author_id")] Guid AuthorId,
    [property: JsonPropertyName("author")] PostAuthor Author,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("image_url")] string? ImageUrl,
    [property: JsonPropertyName("image_urls")] IReadOnlyList<string> ImageUrls,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("is_knowledge")] bool IsKnowledge,
    [property: JsonPropertyName("reactions_count")] int ReactionsCount,
    [property: JsonPropertyName("comments_count")] int CommentsCount,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("saved_by_viewer")] bool SavedByViewer = false,
    [property: JsonPropertyName("reacted_by_viewer")] bool ReactedByViewer = false,
    [property: JsonPropertyName("image_storage_keys")] IReadOnlyList<string>? ImageStorageKeys = null);

public sealed record PostPage(
    [property: JsonPropertyName("items")] IReadOnlyList<PostView> Items,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

public sealed record PostInput(
    string Content,
    string? ImageStorageKey,
    IReadOnlyList<string> ImageStorageKeys,
    string? Link,
    IReadOnlyList<string> Tags,
    PostVisibility Visibility,
    bool IsKnowledge);

public sealed record UpdatePostInput(
    string Content,
    IReadOnlyList<string> ImageStorageKeys,
    IReadOnlyList<string> Tags,
    bool IsKnowledge);

public sealed record CommentView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("post_id")] Guid PostId,
    [property: JsonPropertyName("author_id")] Guid AuthorId,
    [property: JsonPropertyName("author")] PostAuthor Author,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("parent_comment_id")] Guid? ParentCommentId = null,
    [property: JsonPropertyName("likes_count")] int LikesCount = 0,
    [property: JsonPropertyName("liked_by_viewer")] bool LikedByViewer = false);

public sealed record ToggleState(
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("count")] int Count);

public sealed record CommentPage(
    [property: JsonPropertyName("items")] IReadOnlyList<CommentView> Items,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>BE-211: đổi từ ReportTargetKind (3 value) sang ReportTargetType (4 value theo DEC-028).
/// Controller parse string → enum tương ứng.</summary>
public sealed record ReportInputDto(
    ReportTargetType TargetType,
    Guid TargetId,
    string Reason,
    string? Details,
    string? IdempotencyKey);

public interface ISocialService
{
    Task<PostPage> ListPostsAsync(Guid? viewerId, FeedScope scope, string? cursor, int limit, CancellationToken ct);
    Task<PostView> GetPostAsync(Guid viewerId, Guid postId, CancellationToken ct);
    Task<PostView> CreatePostAsync(Guid authorId, PostInput input, CancellationToken ct);
    Task<PostView> UpdatePostAsync(Guid actorId, Guid postId, UpdatePostInput input, CancellationToken ct);
    Task<PostView> UpdatePostVisibilityAsync(Guid actorId, Guid postId, PostVisibility visibility, CancellationToken ct);
    Task DeletePostAsync(Guid actorId, bool isAdmin, Guid postId, CancellationToken ct);
    Task<CommentView> AddCommentAsync(Guid authorId, Guid postId, string text, Guid? parentCommentId, CancellationToken ct);
    Task<CommentPage> ListCommentsAsync(Guid? viewerId, Guid postId, string? cursor, int limit, CancellationToken ct);
    Task<ToggleState> ToggleReactionAsync(Guid userId, Guid postId, ReactionType type, CancellationToken ct);
    Task<ToggleState> ToggleBookmarkAsync(Guid userId, Guid postId, CancellationToken ct);
    Task<ToggleState> ToggleCommentLikeAsync(Guid userId, Guid commentId, CancellationToken ct);

    /// <summary>BE-211: tạo report với idempotency key (optional). Throw ConflictException nếu đã có Open report trùng (DEC-028).</summary>
    Task<Guid> CreateReportAsync(Guid reporterId, ReportInputDto input, string? idempotencyKey, CancellationToken ct);
}
