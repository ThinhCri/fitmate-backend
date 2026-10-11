using Fitmate.Api.Auth;
using Fitmate.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fitmate.Api.Trainers;

[ApiController, Authorize(Roles = "PT"), EnableRateLimiting("AuthLimiter")]
[Route("api/v1/me/payout-account")]
public sealed class PtPayoutAccountController(PtPayoutAccountService service,
    ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await service.GetAsync(current.GetUserIdOrThrow(), ct);
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPut]
    public async Task<IActionResult> Save(SavePtPayoutAccountRequest request, CancellationToken ct) =>
        Ok(await service.SaveAsync(current.GetUserIdOrThrow(), request, ct));
}
