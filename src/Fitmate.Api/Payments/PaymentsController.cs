using Fitmate.Api.Auth;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Payments;

[ApiController]
[Authorize]
[Route("api/v1")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payment;

    public PaymentsController(IPaymentService payment)
    {
        _payment = payment;
    }

    [HttpPost("payments")]
    public async Task<IActionResult> CreatePayment(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] CreatePaymentRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException(ErrorCodes.Validation.RequiredField, "Idempotency-Key is required.", 400);

        var result = await _payment.CreateAsync(current.GetUserIdOrThrow(), body, ct);
        return Created("/payments/" + result.Id, result);
    }

    [HttpGet("me/payments/{id:guid}")]
    public async Task<IActionResult> GetPayment(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var payment = await _payment.GetByIdAsync(current.GetUserIdOrThrow(), id, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpGet("payments/vnpay/return")]
    [AllowAnonymous]
    public IActionResult VnpayReturn(VnpayReturnQuery query)
    {
        return Ok(_payment.HandleVnpayReturn(query));
    }

    [HttpPost("payments/vnpay/ipn")]
    [AllowAnonymous]
    public IActionResult VnpayIpn(VnpayReturnQuery query)
    {
        return Ok(_payment.HandleVnpayIpn(query));
    }
}
