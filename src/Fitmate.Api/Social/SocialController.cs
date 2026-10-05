using System.Security.Claims;
using System.Text.Json.Serialization;
using Fitmate.Application.Social;
using Fitmate.Domain.Social;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Social;

/// <summary>
/// BE-180: Social endpoints — feed, post, comment, reaction, report.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/social")]
public class SocialController : ControllerBase
{
    private readonly ISocialService _svc;
    public SocialController(ISocialService svc) { _svc = svc; }

    [HttpGet("posts")]
    public async Task<IActionResult> ListPosts(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        [FromQuery] string scope = "public",
        CancellationToken ct = default)
    {
        var viewerId = TryGetUserId();
        var feedScope = scope.ToLowerInvariant() switch
        {
            "public" => FeedScope.Public,
            "friends" => FeedScope.Friends,
            _ => throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "scope invalid.",
                new Dictionary<string, string[]> { ["scope"] = new[] { "public|friends" } })
        };
        var page = await _svc.ListPostsAsync(viewerId, feedScope, cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    [HttpGet("posts/{id:guid}")]
    public async Task<IActionResult> GetPost(Guid id, CancellationToken ct)
    {
        var viewerId = GetUserId();
        var post = await _svc.GetPostAsync(viewerId, id, ct);
        return Ok(post);
    }

    [HttpPost("posts")]
    public async Task<IActionResult> CreatePost([FromBody] PostInputDto body, CancellationToken ct)
    {
        var authorId = GetUserId();
        var input = new PostInput(
            Content: body.Content,
            ImageStorageKey: body.ImageStorageKey,
            ImageStorageKeys: body.ImageStorageKeys is { } imageKeys
                ? imageKeys
                : body.ImageStorageKey is null ? Array.Empty<string>() : new[] { body.ImageStorageKey },
            Link: body.Link,
            Tags: body.Tags ?? new List<string>(),
            Visibility: ParseVisibility(body.Visibility),
            IsKnowledge: body.IsKnowledge ?? false);
        var post = await _svc.CreatePostAsync(authorId, input, ct);
        return Created($"/api/v1/social/posts/{post.Id}", post);
    }

    [HttpDelete("posts/{id:guid}")]
    public async Task<IActionResult> DeletePost(Guid id, CancellationToken ct)
    {
        var actorId = GetUserId();
        var isAdmin = User.IsInRole("Admin");
        await _svc.DeletePostAsync(actorId, isAdmin, id, ct);
        return NoContent();
    }

    [HttpGet("bookmarks")]
    public async Task<IActionResult> ListBookmarks(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var page = await _svc.ListBookmarkedPostsAsync(GetUserId(), cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    [HttpPatch("posts/{id:guid}")]
    public async Task<IActionResult> UpdatePost(Guid id, [FromBody] UpdatePostBody body, CancellationToken ct)
        => Ok(await _svc.UpdatePostAsync(GetUserId(), id,
            new UpdatePostInput(body.Content, body.ImageStorageKeys ?? [], body.Tags ?? [], body.IsKnowledge ?? false), ct));

    [HttpPatch("posts/{id:guid}/visibility")]
    public async Task<IActionResult> UpdateVisibility(Guid id, [FromBody] UpdatePostVisibilityBody body, CancellationToken ct)
        => Ok(await _svc.UpdatePostVisibilityAsync(GetUserId(), id, ParseVisibility(body.Visibility), ct));

    [HttpGet("posts/{id:guid}/comments")]
    public async Task<IActionResult> ListComments(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var viewerId = TryGetUserId();
        var page = await _svc.ListCommentsAsync(viewerId, id, cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    [HttpPost("posts/{id:guid}/comments")]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] CreateCommentBody body, CancellationToken ct)
    {
        var authorId = GetUserId();
        var c = await _svc.AddCommentAsync(authorId, id, body.Text, body.ParentCommentId, ct);
        return Created($"/api/v1/social/posts/{id}/comments/{c.Id}", c);
    }

    [HttpPost("posts/{id:guid}/reactions")]
    public async Task<IActionResult> React(Guid id, [FromBody] ReactionBody body, CancellationToken ct)
    {
        var userId = GetUserId();
        var type = body.Type.ToLowerInvariant() switch
        {
            "heart" => ReactionType.Heart,
            "flame" => ReactionType.Flame,
            "smile" => ReactionType.Smile,
            _ => throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "type invalid.",
                new Dictionary<string, string[]> { ["type"] = new[] { "heart|flame|smile" } })
        };
        return Ok(await _svc.ToggleReactionAsync(userId, id, type, ct));
    }

    [HttpPost("posts/{id:guid}/bookmark")]
    public async Task<IActionResult> ToggleBookmark(Guid id, CancellationToken ct)
        => Ok(await _svc.ToggleBookmarkAsync(GetUserId(), id, ct));

    [HttpPost("comments/{id:guid}/like")]
    public async Task<IActionResult> ToggleCommentLike(Guid id, CancellationToken ct)
        => Ok(await _svc.ToggleCommentLikeAsync(GetUserId(), id, ct));

    [HttpPost("reports")]
    public async Task<IActionResult> CreateReport([FromBody] ReportBody body, CancellationToken ct)
    {
        var reporterId = GetUserId();
        var kind = body.TargetType.ToLowerInvariant() switch
        {
            "post" => ReportTargetType.Post,
            "comment" => ReportTargetType.Comment,
            "user" => ReportTargetType.User,
            "certificate" => ReportTargetType.Certificate,
            _ => throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "target_type invalid.",
                new Dictionary<string, string[]> { ["target_type"] = new[] { "post|comment|user|certificate" } })
        };
        // BE-211: idempotency key (optional).
        var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var keyVal)
            ? keyVal.ToString()
            : null;
        if (idempotencyKey is { Length: > 128 or < 8 })
            throw new ValidationException(ErrorCodes.Moderation.IdempotencyKeyInvalid,
                "Idempotency-Key length must be 8..128 chars.",
                new Dictionary<string, string[]> { ["Idempotency-Key"] = new[] { "8..128 chars" } });

        var id = await _svc.CreateReportAsync(reporterId,
            new ReportInputDto(kind, body.TargetId, body.Reason, body.Details, idempotencyKey),
            idempotencyKey, ct);
        return Created($"/api/v1/social/reports/{id}", new { id });
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }

    private Guid? TryGetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static PostVisibility ParseVisibility(string s) => s.ToLowerInvariant() switch
    {
        "public" => PostVisibility.Public,
        "friends" => PostVisibility.Friends,
        "private" => PostVisibility.Private,
        _ => throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "visibility invalid.",
            new Dictionary<string, string[]> { ["visibility"] = new[] { "public|friends|private" } })
    };
}

public sealed record PostInputDto(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("image_storage_key")] string? ImageStorageKey,
    [property: JsonPropertyName("image_storage_keys")] List<string>? ImageStorageKeys,
    [property: JsonPropertyName("link")] string? Link,
    [property: JsonPropertyName("tags")] List<string>? Tags,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("is_knowledge")] bool? IsKnowledge);

public sealed record CreateCommentBody(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parent_comment_id")] Guid? ParentCommentId);

public sealed record UpdatePostVisibilityBody(
    [property: JsonPropertyName("visibility")] string Visibility);

public sealed record UpdatePostBody(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("image_storage_keys")] List<string>? ImageStorageKeys,
    [property: JsonPropertyName("tags")] List<string>? Tags,
    [property: JsonPropertyName("is_knowledge")] bool? IsKnowledge);

public sealed record ReactionBody(
    [property: JsonPropertyName("type")] string Type);

public sealed record ReportBody(
    [property: JsonPropertyName("target_type")] string TargetType,
    [property: JsonPropertyName("target_id")] Guid TargetId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("details")] string? Details);
