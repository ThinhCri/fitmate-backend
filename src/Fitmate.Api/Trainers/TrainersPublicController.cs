using Fitmate.Application.Trainers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

/// <summary>
/// BE-120: Public marketplace endpoints. Theo OpenAPI <c>/trainers</c>.
/// Verified only — PT chưa có chứng chỉ Approved không lộ ra public.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/trainers")]
public class TrainersPublicController : ControllerBase
{
    private readonly ITrainerMarketplaceService _svc;

    public TrainersPublicController(ITrainerMarketplaceService svc)
    {
        _svc = svc;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? specialty = null,
        [FromQuery] long? minPriceVnd = null,
        [FromQuery] long? maxPriceVnd = null,
        [FromQuery] decimal? minRating = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var q = new TrainerMarketplaceQuery(specialty, minPriceVnd, maxPriceVnd, minRating, limit, cursor);
        var (items, next) = await _svc.ListAsync(q, ct);
        return Ok(new { items = items.Select(ToPublicProfile).ToList(), nextCursor = next });
    }

    [HttpGet("{ptUserId:guid}")]
    public async Task<IActionResult> Get(
        Guid ptUserId,
        [FromServices] ITrainerReviewService? reviewService = null,
        CancellationToken ct = default)
    {
        var detail = await _svc.GetDetailAsync(ptUserId, ct);
        var reviews = reviewService is not null
            ? (await reviewService.GetReviewsAsync(ptUserId, 20, ct)).Reviews
            : Array.Empty<ReviewDto>();
        return Ok(new
        {
            id = detail.Profile.UserId,
            full_name = detail.Profile.FullName,
            avatar_url = detail.Profile.AvatarUrl,
            specialty = detail.Profile.Specialty,
            experience_years = detail.Profile.ExperienceYears,
            price_per_session_vnd = detail.Profile.PricePerSessionVnd,
            rating = detail.Profile.Rating,
            reviews_count = detail.Profile.ReviewsCount,
            certificate_status = "verified",
            bio = detail.Bio,
            services = detail.Services.Select(ServicesController.ToResponse).ToList(),
            reviews
        });
    }

    private static PublicTrainerProfileResponse ToPublicProfile(TrainerMarketplaceSummary profile) =>
        new(profile.UserId, profile.FullName, profile.AvatarUrl, profile.Specialty,
            profile.ExperienceYears, profile.PricePerSessionVnd, profile.Rating,
            profile.ReviewsCount, "verified");
}

public sealed record PublicTrainerProfileResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("full_name")] string? FullName,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("specialty")] string? Specialty,
    [property: JsonPropertyName("experience_years")] int? ExperienceYears,
    [property: JsonPropertyName("price_per_session_vnd")] long? PricePerSessionVnd,
    [property: JsonPropertyName("rating")] decimal Rating,
    [property: JsonPropertyName("reviews_count")] int ReviewsCount,
    [property: JsonPropertyName("certificate_status")] string CertificateStatus);
