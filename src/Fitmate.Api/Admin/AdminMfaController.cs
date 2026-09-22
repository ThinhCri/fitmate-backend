using System.Security.Claims;
using Fitmate.Api.Errors;
using Fitmate.Application.Auth;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Admin;

public record MfaVerifyRequest(string Code);
public record MfaDisableRequest(string CodeOrPassword);

/// <summary>
/// BE-803: Admin MFA TOTP enrollment, verification, and recovery.
/// </summary>
[ApiController]
[Route("api/v1/admin/auth/mfa")]
[Authorize(Roles = "Admin")]
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

        var sanitizedCode = request.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var isValid = await _users.VerifyTwoFactorTokenAsync(
            user, _users.Options.Tokens.AuthenticatorTokenProvider, sanitizedCode);

        if (!isValid)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Invalid MFA code",
                ErrorCodes.ValidationFailed, "invalid-mfa-code", "The two-factor authentication code is invalid or expired.", trace);
            return StatusCode(400, prob);
        }

        await _users.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        return Ok(new
        {
            message = "MFA enabled successfully.",
            recovery_codes = recoveryCodes
        });
    }

    [HttpPost("disable")]
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

        var isPasswordValid = !isTokenValid && await _users.CheckPasswordAsync(user, request.CodeOrPassword);

        if (!isTokenValid && !isPasswordValid)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Verification failed",
                ErrorCodes.ValidationFailed, "mfa-disable-verification-failed", "Invalid verification code or password.", trace);
            return StatusCode(400, prob);
        }

        await _users.SetTwoFactorEnabledAsync(user, false);
        return Ok(new { message = "MFA disabled successfully." });
    }

    private Guid? ResolveAdminUserId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}

