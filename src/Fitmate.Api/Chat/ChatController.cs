using System.Security.Claims;
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
        var msg = await _svc.SendFriendMessageAsync(
            senderId,
            body.PeerUserId,
            body.Text,
            body.ClientMessageId,
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

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }
}

public sealed record SendMessageBody(Guid PeerUserId, string Text, Guid? ClientMessageId);
public sealed record MarkReadBody(Guid LastReadMessageId);
