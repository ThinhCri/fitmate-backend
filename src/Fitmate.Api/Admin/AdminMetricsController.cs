using System.Security.Claims;
using Fitmate.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-220: Admin metrics endpoints (OpenAPI <c>/admin/action-required</c>).
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin")]
public class AdminMetricsController : ControllerBase
{
    private readonly IAdminMetricsService _svc;
    public AdminMetricsController(IAdminMetricsService svc) { _svc = svc; }

    [HttpGet("action-required")]
    public async Task<IActionResult> ActionRequired([FromQuery] int recentLimit = 10, CancellationToken ct = default)
    {
        var adminId = GetUserId();
        var result = await _svc.GetActionRequiredAsync(adminId, recentLimit, ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
