using Fitmate.Api.Auth;
using Fitmate.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Identity;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/account-deletions")]
public sealed class AdminAccountDeletionController(AdminAccountDeletionService service, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var (items, total) = await service.ListAsync(current.GetUserIdOrThrow(), page, pageSize, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { items, total, page, pageSize, canStartCountdown = service.CanStartCountdown });
    }

    [HttpPost("{userId:guid}/start")]
    public async Task<IActionResult> Start(Guid userId, CancellationToken ct) =>
        Ok(await service.StartCountdownAsync(current.GetUserIdOrThrow(), userId, ct));

    [HttpPost("{userId:guid}/restore")]
    public async Task<IActionResult> Restore(Guid userId, [FromBody] RestoreDeletionRequest request, CancellationToken ct) =>
        Ok(await service.RestoreAsync(current.GetUserIdOrThrow(), userId, request.Reason, ct));
}

public sealed record RestoreDeletionRequest(string Reason);
