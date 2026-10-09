using System.Text.Json.Serialization;
using Fitmate.Api.Auth;
using Fitmate.Application.Booking;
using Fitmate.Application.Trainers;
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

    /// <summary>Member giữ chỗ (TTL 10 phút).</summary>
    [HttpPost("bookings/holds")]
    public async Task<IActionResult> CreateHold(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] CreateHoldRequest body,
        CancellationToken ct)
    {
        var slot = EnsureUtc(body.SlotStartUtc);
        var (hold, appt) = await _booking.CreateHoldAsync(
            current.GetUserIdOrThrow(), body.PtId, body.ServiceId, slot, ct);
        return CreatedAtAction(nameof(GetHold), new { id = hold.Id }, new CreateBookingHoldResponse(
            hold.Id,
            hold.ExpiresAt,
            appt.Id,
            appt.Status.ToString()
        ));
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
        return Ok(new BookingHoldStatusResponse(hold.Id, hold.ExpiresAt, hold.IsActive));
    }

    /// <summary>Member / PT cancel. Reason ≥ 5 chars.</summary>
    [HttpPost("me/bookings/{id:guid}/cancel")]
    public async Task<ActionResult<AppointmentResponse>> CancelMyBooking(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromBody] CancelBookingRequest body,
        CancellationToken ct)
    {
        var appt = await _booking.CancelAsync(current.GetUserIdOrThrow(), id, body.Reason, ct);
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    /// <summary>PT confirm/accept booking.</summary>
    [HttpPost("me/bookings/{id:guid}/confirm")]
    [HttpPost("bookings/{id:guid}/confirm")]
    [Authorize(Roles = "PT")]
    public async Task<ActionResult<AppointmentResponse>> ConfirmBooking(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var appt = await _booking.ConfirmAsync(current.GetUserIdOrThrow(), id, ct);
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    /// <summary>PT start booking.</summary>
    [HttpPost("me/bookings/{id:guid}/start")]
    [Authorize(Roles = "PT")]
    public async Task<ActionResult<AppointmentResponse>> StartBooking(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var appt = await _booking.StartAsync(current.GetUserIdOrThrow(), id, ct);
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    /// <summary>PT complete.</summary>
    [HttpPost("me/bookings/{id:guid}/complete")]
    [Authorize(Roles = "PT")]
    public async Task<ActionResult<AppointmentResponse>> CompleteMyBooking(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var appt = await _booking.CompleteAsync(current.GetUserIdOrThrow(), id, ct);
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    [HttpPost("me/bookings/{id:guid}/confirm-completion")]
    public async Task<ActionResult<AppointmentResponse>> ConfirmCompletion(
        [FromServices] ICurrentUserAccessor current, Guid id, CancellationToken ct)
    {
        var appt = await _booking.ConfirmCompletionAsync(current.GetUserIdOrThrow(), id, ct);
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    [HttpPost("me/bookings/{id:guid}/dispute")]
    public async Task<ActionResult<AppointmentResponse>> DisputeCompletion(
        [FromServices] ICurrentUserAccessor current, Guid id,
        [FromBody] BookingDisputeRequest body, CancellationToken ct)
    {
        var appt = await _booking.DisputeCompletionAsync(current.GetUserIdOrThrow(), id, body.Reason, ct);
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    /// <summary>Get booking detail by ID.</summary>
    [HttpGet("me/bookings/{id:guid}")]
    public async Task<IActionResult> GetBookingDetail(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var appt = await _booking.FindByIdAsync(id, ct);
        if (appt is null) return NotFound();
        var userId = current.GetUserIdOrThrow();
        if (appt.MemberId != userId && appt.PtUserId != userId) return Forbid();
        return Ok(AppointmentResponse.FromDomain(appt));
    }

    [HttpGet("me/bookings/{id:guid}/meeting")]
    public async Task<IActionResult> GetPrivateMeeting(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] IGoogleMeetConnectionService meet,
        Guid id, CancellationToken ct)
    {
        var uri = await meet.GetMeetingUriForParticipantAsync(current.GetUserIdOrThrow(), id, ct);
        return uri is null ? NotFound() : Ok(new { meeting_url = uri });
    }

    /// <summary>Legacy reschedule route has been retired in favor of two-party reschedule requests (Branch A / BR-05).</summary>
    [HttpPost("me/bookings/{id:guid}/reschedule")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status410Gone)]
    public IActionResult RescheduleLegacy(Guid id)
    {
        return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
        {
            Status = StatusCodes.Status410Gone,
            Title = "Endpoint Retired",
            Detail = "Direct rescheduling has been retired. Please use POST /api/v1/bookings/{id}/reschedule-requests to propose a reschedule under the Two-Party Consent workflow.",
            Extensions = { ["code"] = "BOOKING_RESCHEDULE_ENDPOINT_RETIRED" }
        });
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
        var viewItems = items.Select(AppointmentResponse.FromDomain).ToList();
        return Ok(new AppointmentPageResponse(viewItems, total, normalized.page, normalized.pageSize));
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
        var viewItems = items.Select(AppointmentResponse.FromDomain).ToList();
        return Ok(new AppointmentPageResponse(viewItems, total, normalized.page, normalized.pageSize));
    }

    /// <summary>Create a reschedule request (Member or PT, Branch A rules).</summary>
    [HttpPost("bookings/{id:guid}/reschedule-requests")]
    public async Task<IActionResult> CreateRescheduleRequest(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] IBookingRescheduleService reschedule,
        Guid id,
        [FromBody] CreateRescheduleRequestBody body,
        CancellationToken ct)
    {
        var req = await reschedule.CreateRequestAsync(
            current.GetUserIdOrThrow(),
            id,
            EnsureUtc(body.NewSlotStartUtc),
            body.Reason,
            ct);

        return Ok(BookingRescheduleResponse.FromDomain(req));
    }

    /// <summary>Accept a pending reschedule request (Other party only, atomic slot swap).</summary>
    [HttpPost("bookings/reschedule-requests/{requestId:guid}/accept")]
    public async Task<IActionResult> AcceptRescheduleRequest(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] IBookingRescheduleService reschedule,
        Guid requestId,
        CancellationToken ct)
    {
        var req = await reschedule.AcceptRequestAsync(
            current.GetUserIdOrThrow(),
            requestId,
            ct);

        return Ok(BookingRescheduleResponse.FromDomain(req));
    }

    /// <summary>Reject a pending reschedule request (Other party only).</summary>
    [HttpPost("bookings/reschedule-requests/{requestId:guid}/reject")]
    public async Task<IActionResult> RejectRescheduleRequest(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] IBookingRescheduleService reschedule,
        Guid requestId,
        [FromBody] RejectRescheduleRequestBody? body,
        CancellationToken ct)
    {
        var req = await reschedule.RejectRequestAsync(
            current.GetUserIdOrThrow(),
            requestId,
            body?.RejectionReason,
            ct);

        return Ok(BookingRescheduleResponse.FromDomain(req));
    }

    /// <summary>Cancel a pending reschedule request (Requester only).</summary>
    [HttpPost("bookings/reschedule-requests/{requestId:guid}/cancel")]
    public async Task<IActionResult> CancelRescheduleRequest(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] IBookingRescheduleService reschedule,
        Guid requestId,
        CancellationToken ct)
    {
        var req = await reschedule.CancelRequestAsync(
            current.GetUserIdOrThrow(),
            requestId,
            ct);

        return Ok(BookingRescheduleResponse.FromDomain(req));
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

    private static AppointmentStatus? ParseStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Enum.TryParse<AppointmentStatus>(raw, ignoreCase: true, out var apptStatus) && Enum.IsDefined(apptStatus))
            return apptStatus;
        if (Enum.TryParse<BookingStatus>(raw, ignoreCase: true, out var legacyStatus) && Enum.IsDefined(legacyStatus))
            return Appointment.MapLegacyToAppointmentStatus(legacyStatus);
        return null;
    }
}

public sealed record CreateHoldRequest(
    [property: JsonPropertyName("pt_id")] Guid PtId,
    [property: JsonPropertyName("service_id")] Guid ServiceId,
    [property: JsonPropertyName("slot_start_utc")] DateTime SlotStartUtc
);
public sealed record CreateBookingHoldResponse(
    [property: JsonPropertyName("hold_id")] Guid HoldId,
    [property: JsonPropertyName("hold_expires_at")] DateTime HoldExpiresAt,
    [property: JsonPropertyName("appointment_id")] Guid AppointmentId,
    [property: JsonPropertyName("appointment_status")] string AppointmentStatus
);
public sealed record BookingHoldStatusResponse(
    [property: JsonPropertyName("hold_id")] Guid HoldId,
    [property: JsonPropertyName("expires_at")] DateTime ExpiresAt,
    [property: JsonPropertyName("is_active")] bool IsActive
);
public sealed record CancelBookingRequest([property: JsonPropertyName("reason")] string Reason);
public sealed record BookingDisputeRequest([property: JsonPropertyName("reason")] string Reason);
public sealed record CreateRescheduleRequestBody(
    [property: JsonPropertyName("new_slot_start_utc")] DateTime NewSlotStartUtc,
    [property: JsonPropertyName("reason")] string Reason
);
public sealed record RejectRescheduleRequestBody(
    [property: JsonPropertyName("rejection_reason")] string? RejectionReason
);
