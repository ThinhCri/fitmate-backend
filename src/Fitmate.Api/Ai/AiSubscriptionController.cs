using System.Security.Claims;
using System.Text.Json.Serialization;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Ai;

public sealed record VerifyAiSubscriptionRequest(
    [property: JsonPropertyName("purchase_token")] string PurchaseToken);

[ApiController]
[Authorize]
[Route("api/v1/me/ai-subscription")]
public sealed class AiSubscriptionController(IAiSubscriptionService subscriptions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await subscriptions.GetAsync(UserId(), ct));

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyAiSubscriptionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Thiếu Idempotency-Key hợp lệ.", 400);
        return Ok(await subscriptions.VerifyAsync(UserId(), request.PurchaseToken, ct));
    }

    private Guid UserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return userId;
    }
}
