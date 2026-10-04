using Fitmate.Application.Auth;
using Fitmate.Application.Moderation;
using Fitmate.Application.Trainers;
using Fitmate.Application.Storage;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;
using Microsoft.Extensions.Logging;

namespace Fitmate.Application.Social;

/// <summary>
/// BE-180: Social — post + comment + reaction + report (OpenAPI /social/*).
/// Authorization: mọi endpoint đều yêu cầu authenticated user (Bearer).
/// Visibility filter: scope=public chỉ public; scope=friends bao gồm public + friends của viewer.
/// Reactions: idempotent toggle — nếu đã react thì throw ConflictException (mỗi user chỉ 1 reaction/post).
/// Reports: validate target tồn tại trước khi tạo (Post/Comment/User).
/// </summary>
public class SocialService : ISocialService
{
    private readonly ISocialStore _store;
    private readonly ICertificateStore _certStore;
    private readonly IAuthorizationGuard _authz;
    private readonly IFriendService _friend;
    private readonly IBlockStore _blockStore;
    private readonly ISignedUrlService? _signedUrl;
    private readonly IStorageService? _storage;
    private readonly ILogger<SocialService>? _logger;

    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;
    private const int MaxContentLength = 5000;
    private const int MaxCommentLength = 1000;
    private const int MaxReasonLength = 500;
    private const int MaxDetailsLength = 2000;
    private const int MaxTags = 10;
    private const int MaxTagLength = 32;

    public SocialService(
        ISocialStore store,
        ICertificateStore certStore,
        IAuthorizationGuard authz,
        IFriendService friend,
        IBlockStore blockStore,
        ISignedUrlService? signedUrl = null,
        IStorageService? storage = null,
        ILogger<SocialService>? logger = null)
    {
        _store = store;
        _certStore = certStore;
        _authz = authz;
        _friend = friend;
        _blockStore = blockStore;
        _signedUrl = signedUrl;
        _storage = storage;
        _logger = logger;
    }

    public async Task<PostPage> ListPostsAsync(Guid? viewerId, FeedScope scope, string? cursor, int limit, CancellationToken ct)
    {
        // Feed không yêu cầu role — chỉ cần user hợp lệ (anonymous cũng xem được public).
        if (limit < 1) limit = DefaultLimit;
        if (limit > MaxLimit) limit = MaxLimit;

        var (createdAt, id) = ParseCursor(cursor);
        var (rows, _) = await _store.ListPostsAsync(viewerId, scope, createdAt, id, limit + 1, ct);

        var hasMore = rows.Count > limit;
        var page = hasMore ? rows.Take(limit).ToList() : rows;
        string? next = null;
        if (hasMore && page.Count > 0)
        {
            var last = page[^1];
            next = EncodeCursor(last.CreatedAt, last.Id);
        }

        var views = new List<PostView>(page.Count);
        foreach (var r in page)
        {
            var author = await _store.GetAuthorAsync(r.AuthorId, ct);
            var view = ToView(r, author, viewerId);
            if (viewerId.HasValue)
                view = view with
                {
                    SavedByViewer = await _store.IsBookmarkedAsync(r.Id, viewerId.Value, ct),
                    ReactedByViewer = await _store.HasReactionAsync(r.Id, viewerId.Value, ct)
                };
            views.Add(view);
        }
        return new PostPage(views, next);
    }

    private static void EnsureAuthenticated(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
    }

    public async Task<PostView> GetPostAsync(Guid viewerId, Guid postId, CancellationToken ct)
    {
        EnsureAuthenticated(viewerId);

        var row = await _store.GetPostWithAuthorAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (await _blockStore.IsBlockedEitherWayAsync(viewerId, row.AuthorId, ct))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        // Visibility: private chỉ author xem được; friends cần là friend.
        if (row.Visibility == PostVisibility.Private && row.AuthorId != viewerId)
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        if (row.Visibility == PostVisibility.Friends && row.AuthorId != viewerId)
        {
            var ok = await IsFriendAsync(viewerId, row.AuthorId, ct);
            if (!ok) throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        }

        var author = await _store.GetAuthorAsync(row.AuthorId, ct);
        return ToView(row, author, viewerId) with
        {
            SavedByViewer = await _store.IsBookmarkedAsync(row.Id, viewerId, ct),
            ReactedByViewer = await _store.HasReactionAsync(row.Id, viewerId, ct)
        };
    }

    public async Task<PostView> CreatePostAsync(Guid authorId, PostInput input, CancellationToken ct)
    {
        EnsureAuthenticated(authorId);

        if (string.IsNullOrWhiteSpace(input.Content))
            throw new ValidationException(ErrorCodes.Validation.RequiredField, "content required.", new Dictionary<string, string[]> { ["content"] = new[] { "required" } });
        if (input.Content.Length > MaxContentLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"content > {MaxContentLength} chars.", new Dictionary<string, string[]> { ["content"] = new[] { $"max {MaxContentLength}" } });
        if (input.Tags.Count > MaxTags)
            throw new ValidationException(ErrorCodes.Validation.TooMany, $"tags > {MaxTags}.", new Dictionary<string, string[]> { ["tags"] = new[] { $"max {MaxTags}" } });
        if (input.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > MaxTagLength))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, $"tag invalid.", new Dictionary<string, string[]> { ["tags"] = new[] { "1..32 chars each" } });
        if (input.ImageStorageKeys.Count > 5)
            throw new ValidationException(ErrorCodes.Validation.TooMany, "Mỗi bài viết được đính kèm tối đa 5 ảnh.",
                new Dictionary<string, string[]> { ["image_storage_keys"] = new[] { "max 5 images" } });
        var expectedPrefix = $"posts/{authorId:N}_";
        if (input.ImageStorageKeys.Any(key => string.IsNullOrWhiteSpace(key) || !key.StartsWith(expectedPrefix, StringComparison.Ordinal)))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Ảnh phải do chính tài khoản hiện tại tải lên.",
                new Dictionary<string, string[]> { ["image_storage_keys"] = new[] { "invalid ownership" } });

        var row = new SocialPostRow(
            Id: Guid.NewGuid(),
            AuthorId: authorId,
            Content: input.Content.Trim(),
            ImageStorageKey: input.ImageStorageKey,
            ImageStorageKeys: input.ImageStorageKeys.Distinct(StringComparer.Ordinal).ToList(),
            Link: input.Link,
            Tags: input.Tags.ToList(),
            Visibility: input.Visibility,
            IsKnowledge: input.IsKnowledge,
            ReactionsCount: 0,
            CommentsCount: 0,
            CreatedAt: DateTime.UtcNow);
        await _store.AddPostAsync(row, ct);

        var author = await _store.GetAuthorAsync(authorId, ct);
        return ToView(row, author, authorId);
    }

    public async Task DeletePostAsync(Guid actorId, bool isAdmin, Guid postId, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);
        var row = await _store.GetPostByIdAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (!isAdmin && row.AuthorId != actorId)
            throw new ForbiddenException(ErrorCodes.Social.PostDeleteForbidden, "Cannot delete post of another user.");

        await _store.RemovePostAsync(postId, ct);
    }

    public async Task<PostView> UpdatePostAsync(Guid actorId, Guid postId, UpdatePostInput input, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);
        if (string.IsNullOrWhiteSpace(input.Content))
            throw new ValidationException(ErrorCodes.Validation.RequiredField, "content required.", new Dictionary<string, string[]> { ["content"] = new[] { "required" } });
        if (input.Content.Length > MaxContentLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"content > {MaxContentLength} chars.", new Dictionary<string, string[]> { ["content"] = new[] { $"max {MaxContentLength}" } });
        if (input.Tags.Count > MaxTags)
            throw new ValidationException(ErrorCodes.Validation.TooMany, $"tags > {MaxTags}.", new Dictionary<string, string[]> { ["tags"] = new[] { $"max {MaxTags}" } });
        if (input.Tags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > MaxTagLength))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "tag invalid.", new Dictionary<string, string[]> { ["tags"] = new[] { "1..32 chars each" } });
        if (input.ImageStorageKeys.Count > 5)
            throw new ValidationException(ErrorCodes.Validation.TooMany, "Mỗi bài viết được đính kèm tối đa 5 ảnh.",
                new Dictionary<string, string[]> { ["image_storage_keys"] = new[] { "max 5 images" } });
        var normalizedKeys = input.ImageStorageKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim().Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var expectedPrefix = $"posts/{actorId:N}_";
        if (normalizedKeys.Count != input.ImageStorageKeys.Count || normalizedKeys.Any(key => !key.StartsWith(expectedPrefix, StringComparison.Ordinal)))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Ảnh phải do chính tài khoản hiện tại tải lên.",
                new Dictionary<string, string[]> { ["image_storage_keys"] = new[] { "invalid ownership" } });
        var existing = await _store.GetPostByIdAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        if (existing.AuthorId != actorId)
            throw new ForbiddenException(ErrorCodes.Social.PostDeleteForbidden, "Only the post owner can edit this post.");
        var updated = await _store.UpdatePostAsync(postId, input.Content.Trim(), normalizedKeys, input.Tags.Distinct(StringComparer.Ordinal).ToList(), input.IsKnowledge, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        if (_storage is not null)
        {
            foreach (var removedKey in existing.ImageStorageKeys.Except(normalizedKeys, StringComparer.Ordinal))
            {
                try
                {
                    await _storage.DeleteAsync(removedKey, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The post update is already durable; a transient object-store error must not
                    // turn the response into an ambiguous failure. Operations can retry cleanup.
                    _logger?.LogWarning(ex, "Unable to delete detached post image {StorageKey}", removedKey);
                }
            }
        }
        return ToView(updated, await _store.GetAuthorAsync(actorId, ct), actorId);
    }

    public async Task<PostView> UpdatePostVisibilityAsync(Guid actorId, Guid postId, PostVisibility visibility, CancellationToken ct)
    {
        EnsureAuthenticated(actorId);
        var existing = await _store.GetPostByIdAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        if (existing.AuthorId != actorId)
            throw new ForbiddenException(ErrorCodes.Social.PostDeleteForbidden, "Only the post owner can change visibility.");
        var updated = await _store.UpdatePostVisibilityAsync(postId, visibility, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        return ToView(updated, await _store.GetAuthorAsync(updated.AuthorId, ct), actorId);
    }

    public Task<CommentView> AddCommentAsync(Guid authorId, Guid postId, string text, CancellationToken ct)
        => AddCommentAsync(authorId, postId, text, null, ct);

    public async Task<CommentView> AddCommentAsync(Guid authorId, Guid postId, string text, Guid? parentCommentId, CancellationToken ct)
    {
        EnsureAuthenticated(authorId);

        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException(ErrorCodes.Validation.RequiredField, "text required.", new Dictionary<string, string[]> { ["text"] = new[] { "required" } });
        if (text.Length > MaxCommentLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"text > {MaxCommentLength} chars.", new Dictionary<string, string[]> { ["text"] = new[] { $"max {MaxCommentLength}" } });

        var post = await _store.GetPostByIdAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (await _blockStore.IsBlockedEitherWayAsync(authorId, post.AuthorId, ct))
            throw new ForbiddenException(ErrorCodes.Social.RecipientBlocked, "Cannot comment on post of a blocked user.");

        if (parentCommentId.HasValue)
        {
            var parent = await _store.GetCommentAsync(parentCommentId.Value, ct)
                ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, "Không tìm thấy bình luận được trả lời.");
            if (parent.PostId != postId)
                throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Bình luận trả lời không thuộc bài viết này.",
                    new Dictionary<string, string[]> { ["parent_comment_id"] = new[] { "must belong to the same post" } });
            // Instagram-style thread is one visual level: replies to a reply stay under the root comment.
            parentCommentId = parent.ParentCommentId ?? parent.Id;
        }

        var row = new SocialCommentRow(
            Id: Guid.NewGuid(),
            PostId: postId,
            AuthorId: authorId,
            Text: text.Trim(),
            CreatedAt: DateTime.UtcNow,
            ParentCommentId: parentCommentId);
        await _store.AddCommentAsync(row, ct);
        await _store.ChangePostCommentsAsync(postId, +1, ct);

        var author = await _store.GetAuthorAsync(authorId, ct) ?? new SocialAuthorRow(authorId, "Unknown", null);
        return new CommentView(row.Id, row.PostId, row.AuthorId, new PostAuthor(author.Id, author.DisplayName, author.AvatarUrl), row.Text, row.CreatedAt, row.ParentCommentId, 0, false);
    }

    public async Task<CommentPage> ListCommentsAsync(Guid? viewerId, Guid postId, string? cursor, int limit, CancellationToken ct)
    {
        if (limit < 1) limit = DefaultLimit;
        if (limit > MaxLimit) limit = MaxLimit;

        var post = await _store.GetPostWithAuthorAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (post.Visibility == PostVisibility.Private && (!viewerId.HasValue || post.AuthorId != viewerId.Value))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (post.Visibility == PostVisibility.Friends && (!viewerId.HasValue || (post.AuthorId != viewerId.Value && !await IsFriendAsync(viewerId.Value, post.AuthorId, ct))))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (viewerId.HasValue && await _blockStore.IsBlockedEitherWayAsync(viewerId.Value, post.AuthorId, ct))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        var (createdAt, id) = ParseCursor(cursor);
        var (rows, next) = await _store.ListCommentsAsync(postId, createdAt, id, limit, ct);

        var views = new List<CommentView>(rows.Count);
        foreach (var r in rows)
        {
            var author = await _store.GetAuthorAsync(r.AuthorId, ct);
            var likeState = await _store.GetCommentLikeStateAsync(r.Id, viewerId, ct);
            views.Add(new CommentView(r.Id, r.PostId, r.AuthorId, new PostAuthor(author?.Id ?? r.AuthorId, author?.DisplayName ?? "Unknown", author?.AvatarUrl), r.Text, r.CreatedAt, r.ParentCommentId, likeState.LikesCount, likeState.LikedByViewer));
        }

        return new CommentPage(views, next);
    }

    public async Task<ToggleState> ToggleReactionAsync(Guid userId, Guid postId, ReactionType type, CancellationToken ct)
    {
        EnsureAuthenticated(userId);

        var post = await _store.GetPostByIdAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        if (await _blockStore.IsBlockedEitherWayAsync(userId, post.AuthorId, ct))
            throw new ForbiddenException(ErrorCodes.Social.RecipientBlocked, "Cannot react to post of a blocked user.");

        if (await _store.HasReactionAsync(postId, userId, ct))
        {
            await _store.RemoveReactionAsync(postId, userId, ct);
            var count = await _store.ChangePostReactionsAsync(postId, -1, ct);
            return new ToggleState(false, count);
        }

        var row = new SocialReactionRow(Guid.NewGuid(), postId, userId, type, DateTime.UtcNow);
        await _store.AddReactionAsync(row, ct);
        return new ToggleState(true, await _store.ChangePostReactionsAsync(postId, +1, ct));
    }

    public async Task<ToggleState> ToggleBookmarkAsync(Guid userId, Guid postId, CancellationToken ct)
    {
        EnsureAuthenticated(userId);
        var post = await _store.GetPostByIdAsync(postId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        if (await _blockStore.IsBlockedEitherWayAsync(userId, post.AuthorId, ct))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        var result = await _store.ToggleBookmarkAsync(postId, userId, ct);
        return new ToggleState(result.Saved, result.Saved ? 1 : 0);
    }

    public async Task<ToggleState> ToggleCommentLikeAsync(Guid userId, Guid commentId, CancellationToken ct)
    {
        EnsureAuthenticated(userId);
        var comment = await _store.GetCommentAsync(commentId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, "Không tìm thấy bình luận.");
        var post = await _store.GetPostByIdAsync(comment.PostId, ct)
            ?? throw new NotFoundException(ErrorCodes.Social.PostNotFound, "Không tìm thấy bài viết.");
        if (await _blockStore.IsBlockedEitherWayAsync(userId, post.AuthorId, ct))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, "Không tìm thấy bình luận.");
        var result = await _store.ToggleCommentLikeAsync(commentId, userId, ct);
        return new ToggleState(result.Liked, result.LikesCount);
    }

    public async Task<Guid> CreateReportAsync(Guid reporterId, ReportInputDto input, string? idempotencyKey, CancellationToken ct)
    {
        EnsureAuthenticated(reporterId);

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length < 5)
            throw new ValidationException(ErrorCodes.Validation.TooShort, "reason >= 5 chars.", new Dictionary<string, string[]> { ["reason"] = new[] { "min 5" } });
        if (input.Reason.Length > MaxReasonLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"reason > {MaxReasonLength} chars.", new Dictionary<string, string[]> { ["reason"] = new[] { $"max {MaxReasonLength}" } });
        if (input.Details is { Length: > MaxDetailsLength })
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"details > {MaxDetailsLength} chars.", new Dictionary<string, string[]> { ["details"] = new[] { $"max {MaxDetailsLength}" } });

        // BE-211: Certificate target → chỉ report được cert Approved (visibility check).
        if (input.TargetType == ReportTargetType.Certificate)
        {
            var cert = await _certStore.FindByIdAsync(input.TargetId, ct);
            if (cert is null)
                throw new NotFoundException(ErrorCodes.Social.ReportTargetNotFound, $"Certificate '{input.TargetId}' not found.");
            if (cert.Status != CertificateStatus.Approved)
                throw new ValidationException(ErrorCodes.Moderation.CertificateNotReportable,
                    "Can only report Certificates with status Approved.",
                    new Dictionary<string, string[]> { ["targetId"] = new[] { "approved certificate only" } });
        }
        else
        {
            // Post / Comment / User → generic existence check.
            if (!await _store.TargetExistsAsync(input.TargetType, input.TargetId, ct))
                throw new NotFoundException(ErrorCodes.Social.ReportTargetNotFound, $"{input.TargetType} '{input.TargetId}' not found.");
        }

        // BE-211: Idempotency check.
        // Case 1: client gửi Idempotency-Key → check unique (reporter, key).
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Check if idempotency key already used.
            var existingKeyReport = await _store.FindOpenReportByKeyAsync(reporterId, idempotencyKey, ct);
            if (existingKeyReport is not null)
                throw new ConflictException(ErrorCodes.Moderation.DuplicateReport,
                    $"Report already created with this Idempotency-Key '{idempotencyKey}'.");
        }
        // Case 2: không có key → check spam (DEC-028): 1 user chỉ có 1 Open report cho cùng target.
        else
        {
            var existingOpen = await _store.FindOpenReportAsync(reporterId, input.TargetType, input.TargetId, ct);
            if (existingOpen is not null)
                throw new ConflictException(ErrorCodes.Moderation.DuplicateReport,
                    $"You already have an open report for this target. Report '{existingOpen.Id}'.");
        }

        var newId = await _store.AddReportAsync(reporterId, input.TargetType, input.TargetId, input.Reason, input.Details, idempotencyKey, ct);
        return newId;
    }

    private PostView ToView(SocialPostRow r, SocialAuthorRow? a, Guid? viewerId = null)
    {
        var storageKeys = r.ImageStorageKeys.Count > 0
            ? r.ImageStorageKeys
            : string.IsNullOrWhiteSpace(r.ImageStorageKey) ? [] : [r.ImageStorageKey];
        var imageUrls = storageKeys
            .Select(key => _signedUrl?.CreatePresignedReadUrl(key, TimeSpan.FromMinutes(15)).ToString() ?? key)
            .ToList();

        return new(
            Id: r.Id,
            AuthorId: r.AuthorId,
            Author: new PostAuthor(a?.Id ?? r.AuthorId, a?.DisplayName ?? "Unknown", a?.AvatarUrl),
            Content: r.Content,
            ImageUrl: imageUrls.FirstOrDefault(),
            ImageUrls: imageUrls,
            Link: r.Link,
            Tags: r.Tags,
            Visibility: r.Visibility.ToString().ToLowerInvariant(),
            IsKnowledge: r.IsKnowledge,
            ReactionsCount: r.ReactionsCount,
            CommentsCount: r.CommentsCount,
            CreatedAt: r.CreatedAt,
            ImageStorageKeys: viewerId == r.AuthorId ? storageKeys : []);
    }

    /// <summary>Cursor: base64("<unix-ms>|<guid>"). Ổn định theo (CreatedAt desc, Id desc).</summary>
    internal static string EncodeCursor(DateTime createdAtUtc, Guid id)
    {
        var ms = new DateTimeOffset(createdAtUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var raw = $"{ms}|{id:D}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    internal static (DateTime? createdAt, Guid? id) ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2) throw new FormatException("cursor parts");
            if (!long.TryParse(parts[0], out var ms)) throw new FormatException("cursor ts");
            if (!Guid.TryParse(parts[1], out var id)) throw new FormatException("cursor id");
            return (DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, id);
        }
        catch (Exception ex) when (ex is FormatException)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "cursor invalid.", new Dictionary<string, string[]> { ["cursor"] = new[] { "base64(ts|guid)" } });
        }
    }

    /// <summary>BE-181: delegate Friend check sang FriendService.</summary>
    private Task<bool> IsFriendAsync(Guid viewerId, Guid authorId, CancellationToken ct) => _friend.IsFriendAsync(viewerId, authorId, ct);
}
