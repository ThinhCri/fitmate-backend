using Fitmate.Api.Auth;
using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Booking;

[ApiController]
[Route("api/v1/me/session-packages")]
[Authorize]
public sealed class SessionPackagesController(
    SessionPackageReadService packages, SessionPackageStopService stops,
    ICurrentUserAccessor current) : ControllerBase
{
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
