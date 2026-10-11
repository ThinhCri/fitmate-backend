using System.Security.Claims;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Ai;

[ApiController]
[Authorize]
[Route("api/v1/me/ai-usage")]
public sealed class AiUsageController : ControllerBase
{
    private readonly IAiUsageService _usage;

    public AiUsageController(IAiUsageService usage) => _usage = usage;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return Ok(await _usage.GetAsync(userId, ct));
    }
}
