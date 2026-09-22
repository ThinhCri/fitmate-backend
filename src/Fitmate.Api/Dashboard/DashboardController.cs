using Fitmate.Api.Auth;
using Fitmate.Application.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Dashboard;

[ApiController]
[Authorize]
[Route("api/v1/me/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    /// <summary>
    /// Get the authenticated user's daily progress and dashboard summary.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSummary(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        var summary = await _dashboard.GetSummaryAsync(current.GetUserIdOrThrow(), date, ct);
        return Ok(summary);
    }
}

