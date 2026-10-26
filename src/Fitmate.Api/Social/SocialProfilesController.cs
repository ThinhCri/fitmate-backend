using Fitmate.Api.Auth;
using Fitmate.Application.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Social;

[ApiController]
[Authorize]
[Route("api/v1/social/users/{id:guid}/summary")]
public sealed class SocialProfilesController(SocialProfileSummaryService profiles) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, [FromServices] ICurrentUserAccessor current, CancellationToken ct) =>
        Ok(await profiles.GetAsync(current.GetUserIdOrThrow(), id, ct));
}
