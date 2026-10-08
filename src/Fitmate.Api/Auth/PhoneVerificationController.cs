using System.Text.Json.Serialization;
using Fitmate.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fitmate.Api.Auth;

public sealed record StartPhoneVerificationRequest([property: JsonPropertyName("phone_number")] string PhoneNumber);
public sealed record ConfirmPhoneVerificationRequest([property: JsonPropertyName("code")] string Code);

[ApiController, Authorize, EnableRateLimiting("AuthLimiter")]
[Route("api/v1/me/phone/verification")]
public sealed class PhoneVerificationController(PhoneVerificationService service, ICurrentUserAccessor current) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Start(StartPhoneVerificationRequest request, CancellationToken ct)
    {
        var result = await service.StartAsync(current.GetUserIdOrThrow(), request.PhoneNumber, ct);
        return Accepted(new { expires_at_utc = result.ExpiresAtUtc, retry_after_seconds = result.RetryAfterSeconds });
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(ConfirmPhoneVerificationRequest request, CancellationToken ct)
    {
        await service.ConfirmAsync(current.GetUserIdOrThrow(), request.Code, ct);
        return NoContent();
    }
}
