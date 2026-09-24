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

    [HttpGet("payments/vnpay/ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> VnpayIpnGet(CancellationToken ct)
    {
        var dict = Request.Query.ToDictionary(k => k.Key, v => v.Value.ToString());
        var result = await _payment.HandleVnpayIpnAsync(dict, ct);
        return Ok(result);
    }

    [HttpPost("payments/vnpay/ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> VnpayIpnPost(CancellationToken ct)
    {
        var dict = new Dictionary<string, string>();
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            foreach (var key in form.Keys)
            {
                dict[key] = form[key].ToString();
            }
        }
        else
        {
            foreach (var key in Request.Query.Keys)
            {
                dict[key] = Request.Query[key].ToString();
            }
        }

        var result = await _payment.HandleVnpayIpnAsync(dict, ct);
        return Ok(result);
    }
}
