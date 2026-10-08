using Fitmate.Api.Auth;
using Fitmate.Application.Finance;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

[ApiController, Authorize(Policy = "AdminOnly")]
[Route("api/v1/admin/finance/manual-refunds")]
public sealed class ManualRefundsController(ManualSettlementService service, ICurrentUserAccessor current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await service.ListRefundsAsync(current.GetUserIdOrThrow(), page, pageSize, ct));

    [HttpPost]
    public async Task<IActionResult> Create(CreateRefundRequest request, CancellationToken ct)
    {
        RequireKey();
        return Ok(await service.CreateRefundAsync(current.GetUserIdOrThrow(), request, ct));
    }

    [HttpPost("{refundId:guid}/record-transfer")]
    public async Task<IActionResult> Confirm(Guid refundId, ConfirmManualRefundRequest request, CancellationToken ct)
    {
        RequireKey();
        return Ok(await service.ConfirmRefundAsync(current.GetUserIdOrThrow(), refundId, request, ct));
    }

    [HttpPost("{refundId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid refundId, CancellationToken ct)
    {
        RequireKey();
        return Ok(await service.ApproveRefundAsync(current.GetUserIdOrThrow(), refundId, ct));
    }
    private void RequireKey()
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"]))
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key is required.", 400);
    }
}
