using Fitmate.Api.Auth;
using Fitmate.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController, Authorize(Policy = "AdminOnly"), Route("api/v1/admin/trainer-reviews")]
public sealed class AdminTrainerReviewsController(
    AdminTrainerReviewsService reviews, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? ptUserId, [FromQuery] int? rating,
        [FromQuery] bool? hidden, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) =>
        Ok(await reviews.ListAsync(current.GetUserIdOrThrow(), ptUserId, rating, hidden,
            page, pageSize, ct));

    [HttpPost("{id:guid}/moderation")]
    public async Task<IActionResult> Moderate(Guid id, [FromBody] ModerateTrainerReviewRequest request,
        CancellationToken ct) =>
        Ok(await reviews.ModerateAsync(current.GetUserIdOrThrow(), id, request, ct));
}
