using System.Security.Claims;
using Fitmate.Application.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin/finance/ai-sales")]
public sealed class AdminAiSalesController(IAdminAiSalesService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] AiSalesQuery query, CancellationToken ct)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Ok(await service.ListAsync(Guid.TryParse(id, out var userId) ? userId : Guid.Empty, query, ct));
    }
    [HttpGet("buyers")]
    public async Task<IActionResult> Buyers([FromQuery] AiSalesQuery query, CancellationToken ct)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Ok(await service.BuyersAsync(Guid.TryParse(id, out var userId) ? userId : Guid.Empty, query, ct));
    }
}
