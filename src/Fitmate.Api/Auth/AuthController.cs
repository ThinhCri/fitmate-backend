using System.Security.Claims;
using Fitmate.Api.Errors;
using Fitmate.Application.Auth;
using Fitmate.Application.Auth.Contracts;
using Fitmate.Application.Common;
using Fitmate.Domain.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fitmate.Api.Auth;

/// <summary>
/// Endpoints xác thực theo OpenAPI v1: /auth/register, /auth/login, /auth/refresh, /auth/logout.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole<Guid>> _roles;
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refresh;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RefreshRequest> _refreshValidator;

    public AuthController(
        UserManager<ApplicationUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        SignInManager<ApplicationUser> signIn,
        IJwtTokenService jwt,
        IRefreshTokenService refresh,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<RefreshRequest> refreshValidator)
    {
        _users = users;
        _roles = roles;
        _signIn = signIn;
        _jwt = jwt;
        _refresh = refresh;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _refreshValidator = refreshValidator;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var validation = await _registerValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(409, "Email already exists",
                ErrorCodes.GeneralBadRequest, "email-taken",
                $"Email '{request.Email}' is already registered.", trace);
            return StatusCode(409, prob);
        }

        var user = new ApplicationUser
        {
            Email = request.Email,
            UserName = request.Email,
            FullName = request.FullName,
            Timezone = string.IsNullOrEmpty(request.Timezone) ? "Asia/Ho_Chi_Minh" : request.Timezone,
            CreatedAt = System.DateTime.UtcNow,
            UpdatedAt = System.DateTime.UtcNow,
        };

        var createResult = await _users.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var detail = string.Join("; ", createResult.Errors.Select(e => e.Description));
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Could not create user",
                ErrorCodes.ValidationFailed, "user-create-failed", detail, trace);
            return StatusCode(400, prob);
        }

        var roleName = request.Role.ToLowerInvariant();
        if (!await _roles.RoleExistsAsync(roleName))
            await _roles.CreateAsync(new IdentityRole<Guid>(roleName));
        await _users.AddToRoleAsync(user, roleName);

        var issued = await IssueTokensAsync(user, roleName, null, ct);
        return StatusCode(201, issued);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || user.IsDeleted)
            return MakeUnauthorized("Invalid email or password.");

        if (user.Status == Domain.Common.UserStatus.Locked)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(403, "Account locked",
                ErrorCodes.AuthForbidden, "account-locked",
                "Account is locked. Contact support.", trace);
            return StatusCode(403, prob);
        }

        var signInResult = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(403, "Account locked",
                ErrorCodes.AuthForbidden, "account-locked-too-many-attempts",
                "Too many failed attempts. Try again later.", trace);
            return StatusCode(403, prob);
        }
        if (!signInResult.Succeeded)
            return MakeUnauthorized("Invalid email or password.");

        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "member";
        var issued = await IssueTokensAsync(user, role, request.DeviceLabel, ct);
        return Ok(issued);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRefreshLimiter")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
    {
        var rawToken = request?.RefreshToken;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            rawToken = Request.Cookies["refresh_token"];
        }

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            if (request is not null)
            {
                var validation = await _refreshValidator.ValidateAsync(request, ct);
                if (!validation.IsValid)
                    return BadRequest(ValidationProblem(validation));
            }
            return MakeUnauthorized("Missing refresh token.");
        }

        var rotated = await _refresh.RotateAsync(rawToken, ct);
        if (rotated is null)
        {
            ClearRefreshTokenCookie();
            return MakeUnauthorized("Invalid or expired refresh token.");
        }

        // Resolve userId from the Authorization header claims (caller sends access token too).
        var userId = ResolveUserIdFromToken();
        if (userId is null)
            return MakeUnauthorized("Invalid refresh token.");

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
            return MakeUnauthorized("Invalid refresh token.");

        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "member";
        var access = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email ?? "", role, user.FullName));

        SetRefreshTokenCookie(rotated.RawToken, (int)(rotated.ExpiresAt - DateTime.UtcNow).TotalSeconds);

        return Ok(new AuthResponse(
            access.AccessToken,
            rotated.RawToken,
            access.ExpiresInSeconds,
            ToUserSummary(user, role)));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken ct)
    {
        var rawToken = request?.RefreshToken ?? Request.Cookies["refresh_token"];
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            await _refresh.RevokeAsync(rawToken, ct);
        }
        else
        {
            var userId = ResolveUserIdFromToken();
            if (userId is not null)
            {
                await _refresh.RevokeAllForUserAsync(userId.Value, ct);
            }
        }
        ClearRefreshTokenCookie();
        return NoContent();
    }

    [HttpPost("password/forgot")]
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IValidator<ForgotPasswordRequest> validator,
        [FromServices] Fitmate.Application.Outbox.IOutboxDispatcher outbox,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var user = await _users.FindByEmailAsync(request.Email);
        if (user is not null && !user.IsDeleted && user.Status != Domain.Common.UserStatus.Locked)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            outbox.Enqueue("auth.password_reset_requested", new PasswordResetPayload(user.Id, user.Email!, token, user.FullName));
        }

        // Return 202 Accepted neutrally to prevent email enumeration attacks
        return Accepted(new { message = "If the email address is registered, instructions to reset your password have been sent." });
    }

    [HttpPost("password/reset")]
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        [FromServices] IValidator<ResetPasswordRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            user = await _users.FindByEmailAsync(request.Email);
        }

        if (user is null || user.IsDeleted || user.Status == Domain.Common.UserStatus.Locked)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Invalid or expired token",
                ErrorCodes.ValidationFailed, "invalid-reset-token", "The password reset token is invalid or expired.", trace);
            return StatusCode(400, prob);
        }

        var result = await _users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            var detail = string.Join("; ", result.Errors.Select(e => e.Description));
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Could not reset password",
                ErrorCodes.ValidationFailed, "reset-password-failed", detail, trace);
            return StatusCode(400, prob);
        }

        // Revoke all existing sessions / refresh tokens on password reset for security
        await _refresh.RevokeAllForUserAsync(user.Id, ct);

        return NoContent();
    }

    [HttpPost("password/change")]
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] IValidator<ChangePasswordRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var userId = ResolveUserIdFromToken();
        if (userId is null)
            return MakeUnauthorized("User not found.");

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null || user.IsDeleted || user.Status == Domain.Common.UserStatus.Locked)
            return MakeUnauthorized("User not found or inactive.");

        var result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            var detail = string.Join("; ", result.Errors.Select(e => e.Description));
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(400, "Could not change password",
                ErrorCodes.ValidationFailed, "change-password-failed", detail, trace);
            return StatusCode(400, prob);
        }

        return NoContent();
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, string role, string? deviceLabel, CancellationToken ct)
    {
        var access = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email ?? "", role, user.FullName));
        var refresh = await _refresh.IssueAsync(user.Id, deviceLabel, ct);
        SetRefreshTokenCookie(refresh.RawToken, (int)(refresh.ExpiresAt - DateTime.UtcNow).TotalSeconds);
        return new AuthResponse(
            access.AccessToken,
            refresh.RawToken,
            access.ExpiresInSeconds,
            ToUserSummary(user, role));
    }

    private void SetRefreshTokenCookie(string refreshToken, int expiresInSeconds)
    {
        var isDev = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDev,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            Expires = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)
        };
        Response.Cookies.Append("refresh_token", refreshToken, cookieOptions);
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            Path = "/api/v1/auth"
        });
    }

    private static UserSummaryDto ToUserSummary(ApplicationUser user, string role) =>
        new(user.Id, user.Email ?? "", null, user.FullName, role);

    private Guid? ResolveUserIdFromToken()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static object ValidationProblem(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => string.IsNullOrEmpty(g.Key) ? "_" : g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray());
        return new
        {
            type = "https://docs.fitmate.vn/errors/validation",
            title = "Validation failed",
            status = 400,
            code = ErrorCodes.ValidationFailed,
            detail = "One or more fields failed validation.",
            errors
        };
    }

    private IActionResult MakeUnauthorized(string detail)
    {
        var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
        var prob = FitmateProblemDetails.Create(401, "Unauthorized",
            ErrorCodes.AuthUnauthorized, "unauthorized", detail, trace);
        return StatusCode(401, prob);
    }
}
