using Fitmate.Api.Auth;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Application.Payments;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Booking;

[ApiController]
[Route("api/v1/me/session-packages")]
[Authorize]
public sealed class SessionPackagesController(
    SessionPackageReadService packages, SessionPackageStopService stops,
    SessionPackageHoldService holds, PackagePaymentCheckoutService checkout,
    ICurrentUserAccessor current) : ControllerBase
{
    [HttpPost("holds")]
    public async Task<ActionResult<SessionPackageView>> CreateHold(
        [FromBody] CreateSessionPackageHoldBody body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        RequireIdempotencyKey(idempotencyKey);
        if (body.SlotStartsUtc is null || body.SlotStartsUtc.Count is not (4 or 8))
            throw new DomainException("PACKAGE_SLOTS_REQUIRED", "Choose four or eight session slots.", 400);
        var memberId = current.GetUserIdOrThrow();
        var package = body.DeliveryMode switch
        {
            "in_person" => await holds.CreateInPersonHoldAsync(memberId, body.PtUserId,
                body.ServiceId, body.SlotStartsUtc, ct),
            "online" => await holds.CreateOnlineHoldAsync(memberId, body.PtUserId,
                body.ServiceId, body.SlotStartsUtc, ct),
            _ => throw new DomainException("PACKAGE_DELIVERY_MODE_INVALID",
                "Choose in_person or online delivery.", 400)
        };
        return CreatedAtAction(nameof(Get), new { id = package.Id },
            await packages.GetForParticipantAsync(memberId, package.Id, ct));
    }

    [HttpPost("{id:guid}/payment-requests")]
    public Task<PackageCheckoutView> CreatePayment(Guid id,
        [FromBody] CreatePackagePaymentBody body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        RequireIdempotencyKey(idempotencyKey);
        return checkout.CreateAsync(current.GetUserIdOrThrow(), id, ct,
            body.AcknowledgeShortNotice);
    }
    [HttpGet]
    public Task<SessionPackagePageView> ListMine([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        packages.ListMineAsync(current.GetUserIdOrThrow(), page, pageSize, ct);

    [HttpGet("assigned")]
    [Authorize(Roles = "PT")]
    public Task<SessionPackagePageView> ListAssigned([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        packages.ListAssignedPtAsync(current.GetUserIdOrThrow(), page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<SessionPackageView> Get(Guid id, CancellationToken ct) =>
        packages.GetForParticipantAsync(current.GetUserIdOrThrow(), id, ct);

    [HttpGet("{id:guid}/stop-preview")]
    public Task<SessionPackageStopPreviewView> PreviewStop(Guid id, CancellationToken ct) =>
        stops.PreviewAsync(current.GetUserIdOrThrow(), id, ct);

    [HttpPost("{id:guid}/stop-requests")]
    public async Task<SessionPackageStopView> RequestStop(Guid id,
        [FromBody] RequestPackageStopBody body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        RequireIdempotencyKey(idempotencyKey);
        if (body.ExpectedRefundQuoteVnd is null)
            throw new DomainException(ErrorCodes.Validation.RequiredField,
                "expected_refund_quote_vnd is required.", 400);
        return ToView(await stops.RequestAsync(current.GetUserIdOrThrow(), id, body.Reason,
            ct, body.ExpectedRefundQuoteVnd));
    }

    [HttpPost("{id:guid}/stop-requests/accept")]
    [Authorize(Roles = "PT")]
    public async Task<SessionPackageStopView> AcceptStop(Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        RequireIdempotencyKey(idempotencyKey);
        return ToView(await stops.AcceptByPtAsync(current.GetUserIdOrThrow(), id, ct));
    }

    [HttpPost("{id:guid}/stop-requests/{requestId:guid}/withdraw")]
    public async Task<SessionPackageStopView> WithdrawStop(Guid id, Guid requestId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        RequireIdempotencyKey(idempotencyKey);
        return ToView(await stops.WithdrawAsync(current.GetUserIdOrThrow(), id, requestId, ct));
    }

    private static void RequireIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new DomainException(ErrorCodes.Validation.RequiredField,
                "Idempotency-Key is required.", 400);
    }

    private static SessionPackageStopView ToView(SessionPackageStopRequest request) => new(
        request.Id, request.Reason, request.Status.ToString(), request.ApprovalSource.ToString(),
        request.RequestedAtUtc, request.PtResponseDeadlineUtc, request.RefundQuoteVnd,
        request.FinalizedAtUtc);
}

public sealed record RequestPackageStopBody(
    string Reason,
    [property: JsonPropertyName("expected_refund_quote_vnd")] long? ExpectedRefundQuoteVnd);

public sealed record CreateSessionPackageHoldBody(
    [property: JsonPropertyName("pt_user_id")] Guid PtUserId,
    [property: JsonPropertyName("service_id")] Guid ServiceId,
    [property: JsonPropertyName("delivery_mode")] string DeliveryMode,
    [property: JsonPropertyName("slot_starts_utc")] IReadOnlyList<DateTime> SlotStartsUtc);

public sealed record CreatePackagePaymentBody(
    [property: JsonPropertyName("acknowledge_short_notice")] bool AcknowledgeShortNotice);
