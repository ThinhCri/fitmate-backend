using Fitmate.Api.Auth;
using Fitmate.Application.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController, Authorize(Policy = "AdminOnly"), Route("api/v1/admin")]
public sealed class AdminReviewController(AdminReviewService reviews, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] Guid? targetId, [FromQuery] string? action,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await reviews.AuditAsync(current.GetUserIdOrThrow(), targetId, action, page, pageSize, ct));

    [HttpGet("bookings/{id:guid}/review")]
    public async Task<IActionResult> Booking(Guid id, CancellationToken ct) =>
        Ok(await reviews.BookingAsync(current.GetUserIdOrThrow(), id, ct));

    [HttpGet("trainers/{id:guid}/review")]
    public async Task<IActionResult> Trainer(Guid id, CancellationToken ct) =>
        Ok(await reviews.TrainerAsync(current.GetUserIdOrThrow(), id, ct));
}
