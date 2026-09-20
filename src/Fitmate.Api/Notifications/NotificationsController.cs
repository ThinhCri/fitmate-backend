using System.Security.Claims;
using Fitmate.Application.Notifications;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Notifications;

/// <summary>
/// BE-200: notification list + mark-read endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/me/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _svc;

    public NotificationsController(INotificationService svc) => _svc = svc;

    /// <summary>
    /// List the authenticated user's notifications. Query: read (true|false|omitted), page, pageSize.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? read,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        var result = await _svc.ListMineAsync(userId, read, page, pageSize, ct);
        return Ok(new
        {
            items = result.Items,
            total = result.Total,
        });
    }

    /// <summary>
    /// Mark one notification read. Idempotent. 404 if not owned by current user.
    /// </summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        await _svc.MarkReadAsync(userId, id, ct);
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
