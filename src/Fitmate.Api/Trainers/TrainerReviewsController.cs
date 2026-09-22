using System.Security.Claims;
using Fitmate.Application.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

[ApiController]
[Route("api/v1/trainers/{id:guid}/reviews")]
public class TrainerReviewsController : ControllerBase
{
    private readonly ITrainerReviewService _reviewService;

    public TrainerReviewsController(ITrainerReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> SubmitReview(
        [FromRoute] Guid id,
        [FromBody] CreateReviewRequest request,
        CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId is null) return Unauthorized();

        var review = await _reviewService.SubmitReviewAsync(userId.Value, id, request, ct);
        return CreatedAtAction(nameof(GetReviews), new { id }, review);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetReviews(
        [FromRoute] Guid id,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var summary = await _reviewService.GetReviewsAsync(id, limit, ct);
        return Ok(summary);
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var parsed) ? parsed : null;
    }
}

