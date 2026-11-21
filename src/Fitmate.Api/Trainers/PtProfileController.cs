using System.Text.Json.Serialization;
using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

/// <summary>
/// Endpoint cho PT tự quản lý và cập nhật hồ sơ chuyên môn (tiểu sử, số năm kinh nghiệm, chuyên môn).
/// </summary>
[ApiController]
[Authorize(Roles = "PT")]
[Route("api/v1/me/pt-profile")]
public class PtProfileController : ControllerBase
{
    private readonly IPtProfileService _profileService;

    public PtProfileController(IPtProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    public async Task<ActionResult<PtProfileResponse>> Get(
        [FromServices] ICurrentUserAccessor current,
        CancellationToken ct)
    {
        var userId = current.GetUserIdOrThrow();
        var profile = await _profileService.GetByUserIdAsync(userId, ct)
            ?? await _profileService.EnsureDraftAsync(userId, ct);

        return Ok(ToResponse(profile));
    }

    [HttpPatch]
    public async Task<ActionResult<PtProfileResponse>> Update(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] UpdatePtProfileRequest body,
        CancellationToken ct)
    {
        var userId = current.GetUserIdOrThrow();
        await _profileService.EnsureDraftAsync(userId, ct);

        var existing = await _profileService.GetByUserIdAsync(userId, ct);
        var specialty = body.Specialty ?? existing?.Specialty;
        var expYears = body.ExperienceYears ?? existing?.ExperienceYears;
        var price = body.PricePerSessionVnd ?? existing?.PricePerSessionVnd;
        var bio = body.Bio ?? existing?.Bio;

        var updated = await _profileService.UpdateAsync(userId, specialty, expYears, price, bio, ct);
        return Ok(ToResponse(updated));
    }

    private static PtProfileResponse ToResponse(PtProfile p) =>
        new(
            Id: p.Id,
            UserId: p.UserId,
            Specialty: p.Specialty,
            ExperienceYears: p.ExperienceYears,
            PricePerSessionVnd: p.PricePerSessionVnd,
            Bio: p.Bio,
            Rating: p.Rating,
            ReviewsCount: p.ReviewsCount,
            VerificationStatus: p.VerificationStatus.ToString()
        );
}

public sealed record PtProfileResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("specialty")] string? Specialty,
    [property: JsonPropertyName("experience_years")] int? ExperienceYears,
    [property: JsonPropertyName("price_per_session_vnd")] long? PricePerSessionVnd,
    [property: JsonPropertyName("bio")] string? Bio,
    [property: JsonPropertyName("rating")] decimal Rating,
    [property: JsonPropertyName("reviews_count")] int ReviewsCount,
    [property: JsonPropertyName("verification_status")] string VerificationStatus
);

public sealed record UpdatePtProfileRequest(
    [property: JsonPropertyName("specialty")] string? Specialty = null,
    [property: JsonPropertyName("experience_years")] int? ExperienceYears = null,
    [property: JsonPropertyName("price_per_session_vnd")] long? PricePerSessionVnd = null,
    [property: JsonPropertyName("bio")] string? Bio = null
);
