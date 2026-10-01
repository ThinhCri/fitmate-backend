using System.Security.Claims;
using System.Text.Json.Serialization;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Social;

/// <summary>
/// BE-181: Friend request / accept-reject-cancel / unfriend + friend list + relation status.
/// Theo DEC chốt 2026-09-19: dùng to_user_id, không dùng email.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/social/friends")]
public class FriendsController : ControllerBase
{
    private readonly IFriendService _svc;

    public FriendsController(IFriendService svc) { _svc = svc; }

    [HttpGet("requests")]
    public async Task<IActionResult> ListPendingRequests([FromQuery] string type = "incoming", CancellationToken ct = default)
    {
        var userId = GetUserId();
        var items = await _svc.ListPendingRequestsAsync(userId, type, ct);
        return Ok(new { items });
    }

    [HttpPost("requests")]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestBody body, CancellationToken ct)
    {
        var senderId = GetUserId();
        var view = await _svc.SendRequestAsync(senderId, body.ToUserId, ct);
        return Created($"/api/v1/social/friends/requests/{view.Id}", view);
    }

    [HttpPost("requests/{id:guid}")]
    public async Task<IActionResult> RespondRequest(Guid id, [FromBody] FriendRequestActionBody body, CancellationToken ct)
    {
        var actorId = GetUserId();
        switch (body.Action.ToLowerInvariant())
        {
            case "accept":
                await _svc.AcceptRequestAsync(actorId, id, ct);
                return Ok(new { id, status = "accepted" });
            case "reject":
                await _svc.RejectRequestAsync(actorId, id, ct);
                return Ok(new { id, status = "rejected" });
            case "cancel":
                await _svc.CancelRequestAsync(actorId, id, ct);
                return Ok(new { id, status = "cancelled" });
            default:
                throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "action invalid.",
                    new Dictionary<string, string[]> { ["action"] = new[] { "accept|reject|cancel" } });
        }
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Unfriend(Guid userId, CancellationToken ct)
    {
        var actorId = GetUserId();
        await _svc.UnfriendAsync(actorId, userId, ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> ListFriends(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        var page = await _svc.ListFriendsAsync(userId, cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    [HttpGet("status/{userId:guid}")]
    public async Task<IActionResult> RelationStatus(Guid userId, CancellationToken ct)
    {
        var viewerId = GetUserId();
        var status = await _svc.GetRelationStatusAsync(viewerId, userId, ct);
        return Ok(status);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }
}

public sealed record SendFriendRequestBody([property: JsonPropertyName("to_user_id")] Guid ToUserId);
public sealed record FriendRequestActionBody([property: JsonPropertyName("action")] string Action);
