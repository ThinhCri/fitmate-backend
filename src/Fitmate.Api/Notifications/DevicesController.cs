using System.Security.Claims;
using Fitmate.Application.Notifications;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Notifications;

/// <summary>
/// BE-200: device registration endpoints (push tokens).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/users/me/devices")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceService _svc;

    public DevicesController(IDeviceService svc) => _svc = svc;

    /// <summary>
    /// Register a push device. Idempotent: re-registering the same
    /// (user, token) pair refreshes the row instead of duplicating.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceBody body,
        CancellationToken ct = default)
    {
        if (body is null)
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "Request body is required.",
                new Dictionary<string, string[]>());

        var userId = GetUserId();
        var platform = ParsePlatform(body.Platform);
        var provider = ParseProvider(body.Provider);

        var result = await _svc.RegisterAsync(
            userId,
            body.DeviceToken,
            platform,
            provider,
            ct);

        // The OpenAPI document records this endpoint to return 204, but at
        // runtime we return 200 + the new device id so the FE can use it for
        // subsequent revoke / refresh calls. The schema is updated to match.
        return Ok(new { device_id = result.DeviceId, registered_at = result.RegisteredAt });
    }

    /// <summary>
    /// Revoke (soft) a registered device. Idempotent. 404 if not owned by current user.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        await _svc.RevokeAsync(userId, id, ct);
        return NoContent();
    }

    private static Domain.Common.DevicePlatform ParsePlatform(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "platform is required",
                new Dictionary<string, string[]> { ["platform"] = new[] { "required" } });
        return raw.ToLowerInvariant() switch
        {
            "android" => Domain.Common.DevicePlatform.Android,
            "ios" => Domain.Common.DevicePlatform.Ios,
            "web" => Domain.Common.DevicePlatform.Web,
            _ => throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "platform must be one of: android, ios, web",
                new Dictionary<string, string[]> { ["platform"] = new[] { "out of range" } }),
        };
    }

    private static Domain.Notifications.PushProvider ParseProvider(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "provider is required",
                new Dictionary<string, string[]> { ["provider"] = new[] { "required" } });
        return raw.ToLowerInvariant() switch
        {
            "fcm" => Domain.Notifications.PushProvider.Fcm,
            "expo" => Domain.Notifications.PushProvider.Expo,
            _ => throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "provider must be one of: fcm, expo",
                new Dictionary<string, string[]> { ["provider"] = new[] { "out of range" } }),
        };
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var id) || id == Guid.Empty)
            throw new ForbiddenException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        return id;
    }

    public sealed record RegisterDeviceBody(string DeviceToken, string Platform, string Provider);
}
