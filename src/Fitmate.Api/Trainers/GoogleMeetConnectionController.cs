using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

[ApiController]
[Route("api/v1")]
public sealed class GoogleMeetConnectionController(IGoogleMeetConnectionService meet, ICurrentUserAccessor current) : ControllerBase
{
    [Authorize(Roles = "PT")]
    [HttpGet("me/integrations/google-meet")]
    public async Task<IActionResult> Status(CancellationToken ct) =>
        Ok(await meet.GetStatusAsync(current.GetUserIdOrThrow(), ct));

    [Authorize(Roles = "PT")]
    [HttpPost("me/integrations/google-meet/authorization")]
    public async Task<IActionResult> AuthorizeGoogle(CancellationToken ct) =>
        Ok(new { authorization_url = await meet.CreateAuthorizationUrlAsync(current.GetUserIdOrThrow(), ct) });

    [Authorize(Roles = "PT")]
    [HttpDelete("me/integrations/google-meet")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await meet.DisconnectAsync(current.GetUserIdOrThrow(), ct);
        return NoContent();
    }

    // Google redirects the browser here. The protected one-time state identifies the PT;
    // the callback must not rely on an app JWT or expose the refresh token.
    [AllowAnonymous]
    [HttpGet("integrations/google-meet/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error)) return BadRequest("Google Meet authorization was declined.");
        await meet.CompleteAuthorizationAsync(code ?? string.Empty, state ?? string.Empty, ct);
        return Content("<html><body><h1>Đã kết nối Google Meet</h1><p>Bạn có thể quay lại Fitmate.</p></body></html>", "text/html; charset=utf-8");
    }
}
