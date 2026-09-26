using Fitmate.Api.Auth;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController, Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin/finance/manual-settlements")]
public sealed class ManualSettlementsController(ManualSettlementService service, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await service.ListAsync(current.GetUserIdOrThrow(), page, pageSize, ct));

    [HttpPost("{appointmentId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid appointmentId, ApproveSettlementRequest request, CancellationToken ct)
    {
        RequireKey();
        return Ok(await service.ApproveAsync(current.GetUserIdOrThrow(), appointmentId, request, ct));
    }

    [HttpPost("{appointmentId:guid}/hold")]
    public async Task<IActionResult> Hold(Guid appointmentId, SettlementHoldRequest request, CancellationToken ct)
    {
        RequireKey();
        return Ok(await service.HoldAsync(current.GetUserIdOrThrow(), appointmentId, request.Reason, ct));
    }

    [HttpPost("{appointmentId:guid}/record-transfer")]
    public async Task<IActionResult> RecordTransfer(Guid appointmentId, RecordTransferRequest request, CancellationToken ct)
    {
        RequireKey();
        return Ok(await service.RecordTransferAsync(current.GetUserIdOrThrow(), appointmentId, request, ct));
    }

    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"]))
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key is required.", 400);
    }
}
