using Fitmate.Api.Auth;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Booking;

/// <summary>
/// BE-060: Booking endpoints cho member (booking) và PT (quản lý booking của mình).
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _booking;

    public BookingsController(IBookingService booking)
    {
        _booking = booking;
    }

    /// <summary>Member giữ chỗ (TTL 5 phút).</summary>
    [HttpPost("bookings/holds")]
    public async Task<IActionResult> CreateHold(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] CreateHoldRequest body,
        CancellationToken ct)
    {
        var slot = EnsureUtc(body.SlotStartUtc);
        var (hold, appt) = await _booking.CreateHoldAsync(
            current.GetUserIdOrThrow(), body.PtId, body.ServiceId, slot, ct);
        return CreatedAtAction(nameof(GetHold), new { id = hold.Id }, new
        {
            holdId = hold.Id,
            holdExpiresAt = hold.ExpiresAt,
            appointmentId = appt.Id,
            status = appt.Status.ToString()
        });
    }

    /// <summary>Lookup hold (Owner-scoped). Cho Member kiểm tra TTL trước khi thanh toán.</summary>
    [HttpGet("bookings/holds/{id:guid}")]
    public async Task<IActionResult> GetHold(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromServices] IBookingHoldStore holds,
        CancellationToken ct)
    {
        var hold = await holds.FindByIdAsync(id, ct);
        if (hold is null) return NotFound();
        if (hold.MemberId != current.GetUserIdOrThrow()) return Forbid();
        return Ok(new { holdId = hold.Id, expiresAt = hold.ExpiresAt, isActive = hold.IsActive });
    }

    /// <summary>Member / PT cancel. Reason ≥ 5 chars.</summary>
    [HttpPost("me/bookings/{id:guid}/cancel")]
    public async Task<ActionResult<Appointment>> CancelMyBooking(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromBody] CancelBookingRequest body,
        CancellationToken ct)
    {
        var appt = await _booking.CancelAsync(current.GetUserIdOrThrow(), id, body.Reason, ct);
        return Ok(appt);
    }

    /// <summary>PT complete.</summary>
    [HttpPost("me/bookings/{id:guid}/complete")]
    [Authorize(Roles = "PT")]
    public async Task<ActionResult<Appointment>> CompleteMyBooking(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var appt = await _booking.CompleteAsync(current.GetUserIdOrThrow(), id, ct);
        return Ok(appt);
    }

    /// <summary>Member reschedule: đổi slot booking (transaction check slot mới trước khi release slot cũ).</summary>
    [HttpPost("me/bookings/{id:guid}/reschedule")]
    public async Task<ActionResult<Appointment>> Reschedule(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromBody] RescheduleRequest body,
        CancellationToken ct)
    {
        var appt = await _booking.RescheduleAsync(current.GetUserIdOrThrow(), id, EnsureUtc(body.NewSlotStartUtc), ct);
        return Ok(appt);
    }

    /// <summary>Member list booking của mình (phân trang).</summary>
    [HttpGet("me/bookings")]
    public async Task<IActionResult> ListMyBookings(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var normalized = NormalizePage(page, pageSize);
        var st = ParseStatus(status);
        var (items, total) = await _booking.ListByMemberAsync(current.GetUserIdOrThrow(), st, normalized.page, normalized.pageSize, ct);
        return Ok(new { items, total, page = normalized.page, pageSize = normalized.pageSize });
    }

    /// <summary>PT list booking mà mình nhận.</summary>
    [HttpGet("me/trainer-bookings")]
    [Authorize(Roles = "PT")]
    public async Task<IActionResult> ListMyTrainerBookings(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var normalized = NormalizePage(page, pageSize);
        var st = ParseStatus(status);
        var (items, total) = await _booking.ListByPtAsync(current.GetUserIdOrThrow(), st, normalized.page, normalized.pageSize, ct);
        return Ok(new { items, total, page = normalized.page, pageSize = normalized.pageSize });
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };

    private static (int page, int pageSize) NormalizePage(int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            < 1 => 25,
            > 100 => 100,
            _ => pageSize
        };
        return (page, pageSize);
    }

    private static BookingStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<BookingStatus>(raw, ignoreCase: true, out var s) ? s : null;
    }
}

public sealed record CreateHoldRequest(Guid PtId, Guid ServiceId, DateTime SlotStartUtc);
public sealed record CancelBookingRequest(string Reason);
public sealed record RescheduleRequest(DateTime NewSlotStartUtc);
