using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;

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
    private readonly IAuthorizationGuard _authz;

    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;
    private const int MaxContentLength = 5000;
    private const int MaxCommentLength = 1000;
    private const int MaxReasonLength = 500;
    private const int MaxDetailsLength = 2000;
    private const int MaxTags = 10;
    private const int MaxTagLength = 32;

    public SocialService(ISocialStore store, IAuthorizationGuard authz)
    {
        _store = store;
        _authz = authz;
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
            views.Add(ToView(r, author));
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

        // Visibility: private chỉ author xem được; friends cần là friend.
        if (row.Visibility == PostVisibility.Private && row.AuthorId != viewerId)
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        if (row.Visibility == PostVisibility.Friends && row.AuthorId != viewerId)
        {
            var ok = await IsFriendAsync(viewerId, row.AuthorId, ct);
            if (!ok) throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");
        }

        var author = await _store.GetAuthorAsync(row.AuthorId, ct);
        return ToView(row, author);
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

        var row = new SocialPostRow(
            Id: Guid.NewGuid(),
            AuthorId: authorId,
            Content: input.Content.Trim(),
            ImageStorageKey: input.ImageStorageKey,
            Link: input.Link,
            Tags: input.Tags.ToList(),
            Visibility: input.Visibility,
            IsKnowledge: input.IsKnowledge,
            ReactionsCount: 0,
            CommentsCount: 0,
            CreatedAt: DateTime.UtcNow);
        await _store.AddPostAsync(row, ct);

        var author = await _store.GetAuthorAsync(authorId, ct);
        return ToView(row, author);
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

    public async Task<CommentView> AddCommentAsync(Guid authorId, Guid postId, string text, CancellationToken ct)
    {
        EnsureAuthenticated(authorId);

        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException(ErrorCodes.Validation.RequiredField, "text required.", new Dictionary<string, string[]> { ["text"] = new[] { "required" } });
        if (text.Length > MaxCommentLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"text > {MaxCommentLength} chars.", new Dictionary<string, string[]> { ["text"] = new[] { $"max {MaxCommentLength}" } });

        if (!await _store.PostExistsAsync(postId, ct))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        var row = new SocialCommentRow(
            Id: Guid.NewGuid(),
            PostId: postId,
            AuthorId: authorId,
            Text: text.Trim(),
            CreatedAt: DateTime.UtcNow);
        await _store.AddCommentAsync(row, ct);
        await _store.ChangePostCommentsAsync(postId, +1, ct);

        var author = await _store.GetAuthorAsync(authorId, ct) ?? new SocialAuthorRow(authorId, "Unknown", null);
        return new CommentView(row.Id, row.PostId, row.AuthorId, new PostAuthor(author.Id, author.DisplayName, author.AvatarUrl), row.Text, row.CreatedAt);
    }

    public async Task ToggleReactionAsync(Guid userId, Guid postId, ReactionType type, CancellationToken ct)
    {
        EnsureAuthenticated(userId);

        if (!await _store.PostExistsAsync(postId, ct))
            throw new NotFoundException(ErrorCodes.Social.PostNotFound, $"Post '{postId}' not found.");

        // 1 user chỉ 1 reaction per post → ConflictException 409 nếu đã có.
        if (await _store.HasReactionAsync(postId, userId, ct))
            throw new ConflictException(ErrorCodes.Social.AlreadyReacted, "Already reacted to this post.");

        var row = new SocialReactionRow(Guid.NewGuid(), postId, userId, type, DateTime.UtcNow);
        await _store.AddReactionAsync(row, ct);
        await _store.ChangePostReactionsAsync(postId, +1, ct);
    }

    public async Task<Guid> CreateReportAsync(Guid reporterId, ReportInputDto input, CancellationToken ct)
    {
        EnsureAuthenticated(reporterId);

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length < 5)
            throw new ValidationException(ErrorCodes.Validation.TooShort, "reason >= 5 chars.", new Dictionary<string, string[]> { ["reason"] = new[] { "min 5" } });
        if (input.Reason.Length > MaxReasonLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"reason > {MaxReasonLength} chars.", new Dictionary<string, string[]> { ["reason"] = new[] { $"max {MaxReasonLength}" } });
        if (input.Details is { Length: > MaxDetailsLength })
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"details > {MaxDetailsLength} chars.", new Dictionary<string, string[]> { ["details"] = new[] { $"max {MaxDetailsLength}" } });

        if (!await _store.TargetExistsAsync(input.TargetType, input.TargetId, ct))
            throw new NotFoundException(ErrorCodes.Social.ReportTargetNotFound, $"{input.TargetType} '{input.TargetId}' not found.");

        var id = Guid.NewGuid();
        await _store.AddReportAsync(reporterId, input.TargetType, input.TargetId, input.Reason, input.Details, ct);
        return id;
    }

    private static PostView ToView(SocialPostRow r, SocialAuthorRow? a)
        => new(
            Id: r.Id,
            AuthorId: r.AuthorId,
            Author: new PostAuthor(a?.Id ?? r.AuthorId, a?.DisplayName ?? "Unknown", a?.AvatarUrl),
            Content: r.Content,
            ImageUrl: r.ImageStorageKey,
            Link: r.Link,
            Tags: r.Tags,
            Visibility: r.Visibility.ToString().ToLowerInvariant(),
            IsKnowledge: r.IsKnowledge,
            ReactionsCount: r.ReactionsCount,
            CommentsCount: r.CommentsCount,
            CreatedAt: r.CreatedAt);

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

    /// <summary>Stub: Social friends check sẽ chuyển sang FriendService (BE-181). Hiện false để không lộ post friends-only.</summary>
    private Task<bool> IsFriendAsync(Guid viewerId, Guid authorId, CancellationToken ct) => Task.FromResult(false);
}
