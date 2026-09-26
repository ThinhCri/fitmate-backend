using System.Text.Json;
using Fitmate.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Payments;

[ApiController]
[Route("api/v1/payments/payos/webhook")]
public sealed class PayosWebhookController(IPayosWebhookHandler handler) : ControllerBase
{
    [HttpPost, AllowAnonymous, RequestSizeLimit(32768)]
    public async Task<IActionResult> Receive([FromBody] JsonElement body, CancellationToken ct)
    {
        await handler.HandleAsync(body, ct);
        return Ok(new { code = "00", desc = "success" });
    }
}
