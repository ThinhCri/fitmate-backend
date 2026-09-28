using System.Text.Json.Serialization;
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
    /// PT xem lịch khả dụng authoritative (gồm Open, Held, Booked) của chính mình trong [from, to).
    /// </summary>
    [HttpGet("me/availability")]
    [Authorize(Roles = "PT")]
    public async Task<ActionResult<IReadOnlyList<AvailabilitySlotResponse>>> GetMine(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        var ptUserId = current.GetUserIdOrThrow();
        var fromUtc = EnsureUtc(from);
        var toUtc = EnsureUtc(to);
        var slots = await _availability.ListAuthoritativeForPtAsync(ptUserId, fromUtc, toUtc, ct);
        return Ok(slots.Select(ToResponse).ToList());
    }

    /// <summary>
    /// PT cập nhật lịch khả dụng.
    /// Nếu truyền [from, to] (qua query hoặc body) -> scoped replace: chỉ thay thế Open slots trong [fromUtc, toUtc),
    /// bảo vệ Open slots ngoài range và không đụng Held/Booked slots.
    /// Nếu không truyền [from, to] -> legacy replace toàn bộ slot Open của PT.
    /// </summary>
    [HttpPut("me/availability")]
    [Authorize(Roles = "PT")]
    public async Task<IActionResult> SetMine(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromBody] SetAvailabilityRequest body,
        CancellationToken ct)
    {
        var ptUserId = current.GetUserIdOrThrow();
        var fromUtc = from.HasValue ? EnsureUtc(from.Value) : (body.From.HasValue ? EnsureUtc(body.From.Value) : (DateTime?)null);
        var toUtc = to.HasValue ? EnsureUtc(to.Value) : (body.To.HasValue ? EnsureUtc(body.To.Value) : (DateTime?)null);

        var inputs = (body.Slots ?? Array.Empty<AvailabilitySlotInputDto>())
            .Select(s => new SlotInput(s.SlotStartLocal, s.TrainerTimezone ?? "", s.DurationMinutes))
            .ToList();

        if (fromUtc.HasValue && toUtc.HasValue)
        {
            await _availability.ReplaceScopedAsync(ptUserId, fromUtc.Value, toUtc.Value, inputs, ct);
            return NoContent();
        }

        // Legacy global replace
        await _availability.ReplaceMineAsync(ptUserId, inputs, ct);
        return NoContent();
    }

    /// <summary>
    /// Public xem slot Open của PT trong khoảng [from, to).
    /// </summary>
    [HttpGet("trainers/{ptId:guid}/availability")]
    public async Task<ActionResult<IReadOnlyList<AvailabilitySlotResponse>>> GetForTrainer(
        Guid ptId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        CancellationToken ct)
    {
        var fromUtc = EnsureUtc(from);
        var toUtc = EnsureUtc(to);
        var slots = await _availability.ListOpenForTrainerAsync(ptId, fromUtc, toUtc, ct);
        return Ok(slots.Select(ToResponse).ToList());
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc) // API contract: query date là UTC ISO-8601
        };

    internal static AvailabilitySlotResponse ToResponse(AvailabilitySlot s) =>
        new(s.Id, s.SlotStartUtc, s.SlotStartLocal, s.DurationMinutes, s.Status.ToString().ToLowerInvariant());
}

public sealed record AvailabilitySlotResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("slot_start_utc")] DateTime SlotStartUtc,
    [property: JsonPropertyName("slot_start_local")] DateTime SlotStartLocal,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("status")] string Status);

public sealed record SetAvailabilityRequest(
    [property: JsonPropertyName("slots")] IReadOnlyList<AvailabilitySlotInputDto> Slots,
    [property: JsonPropertyName("from")] DateTime? From = null,
    [property: JsonPropertyName("to")] DateTime? To = null);

public sealed record AvailabilitySlotInputDto(
    [property: JsonPropertyName("slot_start_local")] DateTime SlotStartLocal,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("trainer_timezone")] string? TrainerTimezone = null);
