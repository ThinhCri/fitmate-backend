using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

/// <summary>
/// BE-050: Availability endpoints.
/// </summary>
[ApiController]
[Route("api/v1")]
public class AvailabilityController : ControllerBase
{
    private readonly IAvailabilityService _availability;

    public AvailabilityController(IAvailabilityService availability)
    {
        _availability = availability;
    }

    /// <summary>
    /// PT cập nhật lịch khả dụng (replace toàn bộ slot Open).
    /// </summary>
    [HttpPut("me/availability")]
    [Authorize(Roles = "PT")]
    public async Task<IActionResult> SetMine(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] SetAvailabilityRequest body,
        CancellationToken ct)
    {
        await _availability.ReplaceMineAsync(current.GetUserIdOrThrow(), body.Slots, ct);
        return NoContent();
    }

    /// <summary>
    /// Public xem slot Open của PT trong khoảng [from, to).
    /// </summary>
    [HttpGet("trainers/{ptId:guid}/availability")]
    public async Task<ActionResult<IReadOnlyList<AvailabilitySlot>>> GetForTrainer(
        Guid ptId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        var fromUtc = EnsureUtc(from);
        var toUtc = EnsureUtc(to);
        var slots = await _availability.ListOpenForTrainerAsync(ptId, fromUtc, toUtc, ct);
        return Ok(slots);
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc) // API contract: query date là UTC ISO-8601
        };
}

public sealed record SetAvailabilityRequest(IReadOnlyList<SlotInput> Slots);
