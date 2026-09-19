using System.Security.Claims;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Social;

/// <summary>
/// BE-181: Block / Unblock / List blocked users.
/// Theo block policy: không ảnh hưởng booking/finance; huỷ pending friend requests.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/social/blocks")]
public class BlocksController : ControllerBase
{
    private readonly IBlockService _svc;

    public BlocksController(IBlockService svc) { _svc = svc; }

    [HttpPost]
    public async Task<IActionResult> Block([FromBody] BlockBody body, CancellationToken ct)
    {
        var blockerId = GetUserId();
        await _svc.BlockAsync(blockerId, body.UserId, ct);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken ct)
    {
        var blockerId = GetUserId();
        await _svc.UnblockAsync(blockerId, userId, ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var blockerId = GetUserId();
        var page = await _svc.ListBlockedAsync(blockerId, cursor, limit, ct);
        return Ok(new { items = page.Items, nextCursor = page.NextCursor });
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }
}

public sealed record BlockBody(Guid UserId);
