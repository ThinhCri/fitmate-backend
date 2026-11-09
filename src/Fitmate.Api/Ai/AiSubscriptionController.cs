using System.Security.Claims;
using System.Text.Json.Serialization;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fitmate.Api.Ai;

public sealed record VerifyAiSubscriptionRequest(
    [property: JsonPropertyName("purchase_token")] string PurchaseToken);

[ApiController]
[Authorize]
[Route("api/v1/me/ai-subscription")]
public sealed class AiSubscriptionController(IAiSubscriptionService subscriptions, IOptions<GooglePlayBillingOptions> options, IAiOneTimePurchaseService? purchases = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(ForClient(await subscriptions.GetAsync(UserId(), ct)));

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyAiSubscriptionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Thiếu Idempotency-Key hợp lệ.", 400);
        await subscriptions.VerifyAsync(UserId(), request.PurchaseToken, ct);
        return Ok(ForClient(await subscriptions.GetAsync(UserId(), ct)));
    }

    [HttpPost("verify-purchase")]
    public async Task<IActionResult> VerifyPurchase([FromBody] VerifyAiSubscriptionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new DomainException("IDEMPOTENCY_KEY_REQUIRED", "Thiếu Idempotency-Key hợp lệ.", 400);
        if (purchases is null) throw new DomainException("AI_BILLING_UNAVAILABLE", "Chưa thể xác minh giao dịch.", 503);
        await purchases.VerifyAsync(UserId(), request.PurchaseToken, ct);
        return Ok(ForClient(await subscriptions.GetAsync(UserId(), ct)));
    }

    private AiSubscriptionView ForClient(AiSubscriptionView view)
    {
        var build = Request.Headers["X-Fitmate-Android-Version-Code"].ToString();
        if (!int.TryParse(build, out var code) || code < options.Value.OneTimeMinimumAndroidVersionCode)
            return view with { BillingAvailable = false, ProductId = options.Value.ProductId,
                BasePlanId = options.Value.BasePlanId, PurchaseType = "subs" };
        return AiPlusSalesPolicy.ForClient(view, build, options.Value.MinimumAndroidVersionCode);
    }

    private Guid UserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(raw, out var userId) || userId == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return userId;
    }
}
