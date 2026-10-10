using Fitmate.Api.Auth;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController]
[Route("api/v1/admin/finance/booking-incidents")]
[Authorize(Roles = "Admin")]
public sealed class AdminBookingIncidentsController(
    ManualSettlementService finance, ICurrentUserAccessor current) : ControllerBase
{
    [HttpPost("{ticketId:guid}/resolve")]
    public Task<BookingIncidentDecisionView> Resolve(Guid ticketId,
        [FromBody] ResolveBookingIncidentRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"]))
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key is required.", 400);
        return finance.ResolveBookingIncidentAsync(current.GetUserIdOrThrow(), ticketId, body, ct);
    }
}
