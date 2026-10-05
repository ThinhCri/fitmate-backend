using System.Security.Claims;
using System.Text.Json.Serialization;
using Fitmate.Application.Chat;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Chat;

/// <summary>
/// BE-191: friend chat endpoints (5 routes). AI chat không thuộc scope này.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/chat")]
public class ChatController : ControllerBase
{
    private readonly IChatService _svc;
    public ChatController(IChatService svc) { _svc = svc; }

    [HttpPost("friends/messages")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageBody body, CancellationToken ct)
    {
        var senderId = GetUserId();
        var msg = await _svc.SendFriendMessageWithImagesAsync(
            senderId,
            body.PeerUserId,
            body.Text,
            body.ClientMessageId,
            body.ImageStorageKeys ?? new List<string>(),
            ct);
        return Created($"/api/v1/chat/friends/conversations/{msg.ConversationId}", msg);
    }

    [HttpGet("friends/conversations")]
    public async Task<IActionResult> ListConversations(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 30,
        CancellationToken ct = default)
    {
        var viewerId = GetUserId();
        var page = await _svc.ListConversationsAsync(viewerId, cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    [HttpGet("friends/conversations/{id:guid}")]
    public async Task<IActionResult> GetConversation(Guid id, CancellationToken ct)
    {
        var viewerId = GetUserId();
        var conv = await _svc.GetConversationAsync(viewerId, id, ct);
        return Ok(conv);
    }

    [HttpGet("friends/conversations/{id:guid}/messages")]
    public async Task<IActionResult> ListMessages(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 30,
        CancellationToken ct = default)
    {
        var viewerId = GetUserId();
        var page = await _svc.ListMessagesAsync(viewerId, id, cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    [HttpPost("friends/conversations/{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, [FromBody] MarkReadBody body, CancellationToken ct)
    {
        var viewerId = GetUserId();
        await _svc.MarkReadAsync(viewerId, id, body.LastReadMessageId, ct);
        return NoContent();
    }

    [HttpPost("friends/messages/{id:guid}/reactions")]
    public async Task<IActionResult> ToggleReaction(Guid id, [FromBody] ToggleMessageReactionBody body, CancellationToken ct)
        => Ok(new { items = await _svc.ToggleReactionAsync(GetUserId(), id, body.Reaction, ct) });

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }
}

public sealed record SendMessageBody(
    [property: JsonPropertyName("peer_user_id")] Guid PeerUserId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("client_message_id")] Guid? ClientMessageId,
    [property: JsonPropertyName("image_storage_keys")] List<string>? ImageStorageKeys = null);

public sealed record MarkReadBody(
    [property: JsonPropertyName("last_read_message_id")] Guid LastReadMessageId);

public sealed record ToggleMessageReactionBody(
    [property: JsonPropertyName("reaction")] string Reaction);
