using Fitmate.Application.Trainers;
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
        return Ok(new { items, nextCursor = next });
    }

    [HttpGet("{ptUserId:guid}")]
    public async Task<IActionResult> Get(Guid ptUserId, CancellationToken ct)
    {
        var detail = await _svc.GetDetailAsync(ptUserId, ct);
        return Ok(new
        {
            profile = detail.Profile,
            bio = detail.Bio,
            services = detail.Services.Select(ServicesController.ToResponse).ToList()
        });
    }
}
