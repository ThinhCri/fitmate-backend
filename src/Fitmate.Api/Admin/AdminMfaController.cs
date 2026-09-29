using System.Security.Claims;
using Fitmate.Api.Errors;
using Fitmate.Application.Auth;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fitmate.Api.Admin;

public record MfaVerifyRequest(string Code);
public record MfaDisableRequest(string CodeOrPassword);

/// <summary>
/// BE-803: Admin MFA TOTP enrollment, verification, and recovery.
/// </summary>
[ApiController]
[Route("api/v1/admin/auth/mfa")]
[Authorize(Roles = "Admin,MfaSetup")]
[EnableRateLimiting("AuthLimiter")]
public class AdminMfaController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuthorizationGuard _authz;

    public AdminMfaController(UserManager<ApplicationUser> users, IAuthorizationGuard authz)
    {
        _users = users;
        _authz = authz;
    }

    [HttpPost("setup")]
    public async Task<IActionResult> SetupMfa(CancellationToken ct)
    {
        var userId = ResolveAdminUserId();
        if (userId is null)
            return Unauthorized();

        await _authz.EnsureRoleAsync(userId.Value, "Admin", ct);

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null || user.IsDeleted || user.Status == Domain.Common.UserStatus.Locked)
            return Unauthorized();

        if (user.TwoFactorEnabled) return Conflict(new { code = "MFA_ALREADY_ENABLED", detail = "An active authenticator secret cannot be retrieved." });
        Response.Headers.CacheControl = "no-store";
        var unformattedKey = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await _users.GetAuthenticatorKeyAsync(user);
        }

        var email = user.Email ?? "admin@fitmate.vn";
        var authenticatorUri = $"otpauth://totp/Fitmate:{Uri.EscapeDataString(email)}?secret={unformattedKey}&issuer=Fitmate&digits=6";

        return Ok(new
        {
            shared_key = unformattedKey,
            authenticator_uri = authenticatorUri
        });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyAndEnableMfa([FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { message = "MFA verification code is required." });

        var userId = ResolveAdminUserId();
        if (userId is null)
            return Unauthorized();

        await _authz.EnsureRoleAsync(userId.Value, "Admin", ct);

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null || user.IsDeleted || user.Status == Domain.Common.UserStatus.Locked)
            return Unauthorized();

        if (user.TwoFactorEnabled) return Conflict(new { code = "MFA_ALREADY_ENABLED" });
        if (await _users.IsLockedOutAsync(user)) return Unauthorized();
        var sanitizedCode = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isValid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, sanitizedCode);

        if (!isValid)
        {
            await _users.AccessFailedAsync(user);
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Invalid MFA code",
                ErrorCodes.ValidationFailed, "invalid-mfa-code", "The two-factor authentication code is invalid or expired.", trace);
            return StatusCode(400, prob);
        }

        await _users.SetTwoFactorEnabledAsync(user, true);
        await _users.UpdateSecurityStampAsync(user);
        await HttpContext.RequestServices.GetRequiredService<IRefreshTokenService>().RevokeAllForUserAsync(user.Id, ct);
        var recoveryCodes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        Response.Headers.CacheControl = "no-store";

        return Ok(new
        {
            message = "MFA enabled successfully.",
            recovery_codes = recoveryCodes
        });
    }

    [HttpPost("disable")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DisableMfa([FromBody] MfaDisableRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CodeOrPassword))
            return BadRequest(new { message = "Code or password confirmation is required to disable MFA." });

        var userId = ResolveAdminUserId();
        if (userId is null)
            return Unauthorized();

        await _authz.EnsureRoleAsync(userId.Value, "Admin", ct);

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null || user.IsDeleted || user.Status == Domain.Common.UserStatus.Locked)
            return Unauthorized();

        var sanitized = request.CodeOrPassword.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isTokenValid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, sanitized);

        if (!isTokenValid)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Verification failed",
                ErrorCodes.ValidationFailed, "mfa-disable-verification-failed", "Invalid verification code or password.", trace);
            return StatusCode(400, prob);
        }

        await _users.SetTwoFactorEnabledAsync(user, false);
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.UpdateSecurityStampAsync(user);
        await HttpContext.RequestServices.GetRequiredService<IRefreshTokenService>().RevokeAllForUserAsync(user.Id, ct);
        return Ok(new { message = "MFA disabled successfully." });
    }

    [HttpGet("status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var id = ResolveAdminUserId();
        if (!id.HasValue) return Unauthorized();
        await _authz.EnsureRoleAsync(id.Value, "Admin", ct);
        var user = await _users.FindByIdAsync(id.Value.ToString());
        if (user is null) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        return Ok(new { enabled = user.TwoFactorEnabled });
    }

    private Guid? ResolveAdminUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

