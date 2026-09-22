using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

/// <summary>
/// BE-818: Admin booking management endpoints (OpenAPI /admin/bookings).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/bookings")]
public class AdminBookingsController : ControllerBase
{
    private readonly IBookingService _booking;

    public AdminBookingsController(IBookingService booking)
    {
        _booking = booking;
    }

    /// <summary>
    /// List all bookings with filters for status, PT, member, and pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] Guid? ptUserId,
        [FromQuery] Guid? memberId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var st = ParseStatus(status);
        var (items, total) = await _booking.ListAdminAsync(st, ptUserId, memberId, page, pageSize, ct);
        return Ok(new { items, total, page, pageSize });
    }

    /// <summary>
    /// Get single booking detail by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var appt = await _booking.FindByIdAsync(id, ct);
        if (appt is null) return NotFound();
        return Ok(appt);
    }

    private static BookingStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<BookingStatus>(raw, ignoreCase: true, out var s) ? s : null;
    }
}
