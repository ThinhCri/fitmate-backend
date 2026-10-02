using System.Security.Claims;
using Fitmate.Api.Errors;
using Fitmate.Application.Auth;
using Fitmate.Application.Auth.Contracts;
using Fitmate.Application.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Api.Auth;

/// <summary>
/// Endpoints xác thực theo OpenAPI v1: /auth/register, /auth/login, /auth/refresh, /auth/logout.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private IAuthSessionService Sessions => HttpContext.RequestServices.GetRequiredService<IAuthSessionService>();
    private IAdminLoginChallengeService Challenges => HttpContext.RequestServices.GetRequiredService<IAdminLoginChallengeService>();
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
        if (!CookieRequestAllowed()) return Forbid();
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

        if (roleName == "pt")
        {
            var db = HttpContext.RequestServices.GetRequiredService<FitmateDbContext>();
            if (!await db.PtProfiles.AnyAsync(p => p.UserId == user.Id, ct))
            {
                db.PtProfiles.Add(PtProfile.CreateDraft(
                    user.Id,
                    request.Specialty,
                    request.ExperienceYears,
                    request.PricePerSessionVnd,
                    bio: null));
            }
        }

        var code = await _users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, "email-verification");
        var protector = HttpContext.RequestServices.GetRequiredService<Fitmate.Application.Email.IEmailTokenProtector>();
        var outbox = HttpContext.RequestServices.GetRequiredService<Fitmate.Application.Outbox.IOutboxDispatcher>();
        outbox.Enqueue("auth.email_verification", new Fitmate.Application.Email.Handlers.EmailVerificationPayloadDto(
            user.Id, user.Email!, protector.Protect(code), user.FullName, DateTime.UtcNow.AddMinutes(5)));
        await HttpContext.RequestServices.GetRequiredService<FitmateDbContext>().SaveChangesAsync(ct);

        return StatusCode(201, new RegistrationPendingResponse(true, user.Email!, 300));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!CookieRequestAllowed()) return Forbid();
        var validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || user.IsDeleted)
            return MakeUnauthorized("Invalid email or password.");

        if (user.Status != Domain.Common.UserStatus.Active)
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

        // Do not reveal registration/verification state until ownership is proven
        // with the correct password. This prevents account enumeration and OTP spam.
        if (!user.EmailConfirmed)
        {
            var trace = FitmateProblemDetails.ResolveTraceId(HttpContext);
            var prob = FitmateProblemDetails.Create(403, "Email verification required",
                ErrorCodes.AuthEmailNotVerified, "email-not-verified",
                "Verify your email address before signing in.", trace);
            return StatusCode(403, prob);
        }

        var role = PreferredRole(await _users.GetRolesAsync(user));
        if (role.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            if (!user.TwoFactorEnabled)
            {
                // This token can only enroll MFA; it never carries the Admin role.
                var setup = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email!, "MfaSetup", user.FullName,
                    user.SecurityStamp, "mfa_setup", 300));
                return Accepted(new { mfaSetupRequired = true, setupToken = setup.AccessToken });
            }
            var challenge = await Challenges.CreateAsync(user.Id, user.SecurityStamp!, ct);
            return Accepted(new { mfaRequired = true, challenge.ChallengeId, challenge.ExpiresIn });
        }
        var issued = await IssueTokensAsync(user, role, request.DeviceLabel, ct);
        return Ok(ClientResponse(issued));
    }

    [HttpPost("/api/v1/admin/auth/login/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> VerifyLogin([FromBody] VerifyAdminLoginRequest request, CancellationToken ct)
    {
        if (!CookieRequestAllowed()) return Forbid();
        var id = await Challenges.VerifyAsync(request.ChallengeId, request.Code, request.UseRecoveryCode, ct);
        var user = id.HasValue ? await _users.FindByIdAsync(id.Value.ToString()) : null;
        if (user is null) return MakeUnauthorized("Invalid or expired verification challenge.");
        return Ok(ClientResponse(await IssueTokensAsync(user, "Admin", "admin-web", ct)));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthRefreshLimiter")]
    public async Task<IActionResult> Refresh([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RefreshRequest? request, CancellationToken ct)
    {
        if (!CookieRequestAllowed(!string.IsNullOrWhiteSpace(request?.RefreshToken))) return Forbid();
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

        // Identity comes from the validated refresh session, never an expired access token.
        var user = await _users.FindByIdAsync(rotated.UserId.ToString());
        if (user is null || user.IsDeleted || user.Status != Domain.Common.UserStatus.Active || await _users.IsLockedOutAsync(user))
        {
            await _refresh.RevokeAsync(rotated.RawToken, ct);
            ClearRefreshTokenCookie();
            return MakeUnauthorized("Invalid refresh token.");
        }

        var role = PreferredRole(await _users.GetRolesAsync(user));
        var isAdmin = role.Equals("admin", StringComparison.OrdinalIgnoreCase);
        var session = rotated.SessionId.HasValue
            ? await Sessions.FindActiveAsync(rotated.SessionId.Value, user.Id, user.SecurityStamp!, ct) : null;
        if (session is null || (isAdmin && (!user.TwoFactorEnabled || session.AuthenticationMethod != "mfa" || session.MfaVerifiedAt is null)))
        {
            await _refresh.RevokeAsync(rotated.RawToken, ct);
            ClearRefreshTokenCookie();
            return MakeUnauthorized("Admin MFA login is required.");
        }
        var access = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email ?? "", role, user.FullName,
            user.SecurityStamp, session.AuthenticationMethod, SessionId: session.Id));

        SetRefreshTokenCookie(rotated.RawToken, (int)(rotated.ExpiresAt - DateTime.UtcNow).TotalSeconds);

        return Ok(ClientResponse(new AuthResponse(
            access.AccessToken,
            rotated.RawToken,
            access.ExpiresInSeconds,
            ToUserSummary(user, role, await GetPtVerificationStatusAsync(user.Id, role, ct)))));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] LogoutRequest? request, CancellationToken ct)
    {
        if (!CookieRequestAllowed(!string.IsNullOrWhiteSpace(request?.RefreshToken))) return Forbid();
        var rawToken = request?.RefreshToken ?? Request.Cookies["refresh_token"];
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            await _refresh.RevokeAsync(rawToken, ct);
        }
        ClearRefreshTokenCookie();
        var actorId = ResolveUserIdFromToken();
        if (actorId.HasValue && Guid.TryParse(User.FindFirstValue("sid"), out var sessionId))
            await Sessions.RevokeAsync(sessionId, actorId.Value, ct);
        return NoContent();
    }

    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IValidator<ForgotPasswordRequest> validator,
        [FromServices] Fitmate.Application.Outbox.IOutboxDispatcher outbox,
        [FromServices] Fitmate.Application.Email.IEmailTokenProtector protector,
        [FromServices] FitmateDbContext db,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var user = await _users.FindByEmailAsync(request.Email);
        if (user is not null && !user.IsDeleted && user.Status != Domain.Common.UserStatus.Locked)
        {
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            outbox.Enqueue("auth.password_reset_requested", new Fitmate.Application.Email.Handlers.PasswordResetPayloadDto(
                user.Id, user.Email!, protector.Protect(token), user.FullName, DateTime.UtcNow.AddMinutes(15)));
            await db.SaveChangesAsync(ct);
        }

        // Return 202 Accepted neutrally to prevent email enumeration attacks
        return Accepted(new { message = "If the email address is registered, instructions to reset your password have been sent." });
    }

    [HttpPost("email/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        [FromServices] IValidator<VerifyEmailRequest> validator,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(ValidationProblem(validation));

        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || user.IsDeleted || user.EmailConfirmed)
            return MakeUnauthorized("Invalid or expired verification code.");

        var valid = await _users.VerifyUserTokenAsync(
            user, TokenOptions.DefaultEmailProvider, "email-verification", request.Code);
        if (!valid) return MakeUnauthorized("Invalid or expired verification code.");

        user.EmailConfirmed = true;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
            return StatusCode(500, FitmateProblemDetails.Create(500, "Email verification failed",
                ErrorCodes.GeneralBadRequest, "email-verification-failed",
                "The email address could not be verified.", FitmateProblemDetails.ResolveTraceId(HttpContext)));

        return NoContent();
    }

    [HttpPost("email/resend")]
    [AllowAnonymous]
    [EnableRateLimiting("AuthLimiter")]
    public async Task<IActionResult> ResendEmailVerification(
        [FromBody] ResendEmailVerificationRequest request,
        [FromServices] IValidator<ResendEmailVerificationRequest> validator,
        [FromServices] Fitmate.Application.Outbox.IOutboxDispatcher outbox,
        [FromServices] Fitmate.Application.Email.IEmailTokenProtector protector,
        [FromServices] FitmateDbContext db,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(ValidationProblem(validation));

        var user = await _users.FindByEmailAsync(request.Email);
        if (user is not null && !user.IsDeleted && !user.EmailConfirmed && user.Status == Domain.Common.UserStatus.Active)
        {
            var code = await _users.GenerateUserTokenAsync(user, TokenOptions.DefaultEmailProvider, "email-verification");
            outbox.Enqueue("auth.email_verification", new Fitmate.Application.Email.Handlers.EmailVerificationPayloadDto(
                user.Id, user.Email!, protector.Protect(code), user.FullName, DateTime.UtcNow.AddMinutes(5)));
            await db.SaveChangesAsync(ct);
        }

        return Accepted(new { message = "If the account is eligible, a verification code has been sent." });
    }

    [HttpPost("password/reset")]
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
    [HttpPost("/api/v1/users/me/password")]
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

        await _refresh.RevokeAllForUserAsync(user.Id, ct);
        ClearRefreshTokenCookie();
        return NoContent();
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, string role, string? deviceLabel, CancellationToken ct)
    {
        var isAdmin = role.Equals("admin", StringComparison.OrdinalIgnoreCase);
        var session = await Sessions.CreateAsync(user.Id, user.SecurityStamp!, isAdmin, ct);
        var access = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email ?? "", role, user.FullName,
            user.SecurityStamp, session.AuthenticationMethod, SessionId: session.Id));
        var refresh = await _refresh.IssueForSessionAsync(user.Id, deviceLabel, session.Id, ct);
        SetRefreshTokenCookie(refresh.RawToken, (int)(refresh.ExpiresAt - DateTime.UtcNow).TotalSeconds);
        return new AuthResponse(
            access.AccessToken,
            refresh.RawToken,
            access.ExpiresInSeconds,
            ToUserSummary(user, role, await GetPtVerificationStatusAsync(user.Id, role, ct)));
    }

    private bool IsWeb => Request.Headers["X-Fitmate-Client"] == "admin-web";

    // A custom header forces browser cross-origin preflight. CORS must allow only trusted origins.
    private bool CookieRequestAllowed(bool nativeToken = false) => IsWeb ||
        (!Request.Headers.ContainsKey("Origin") && (nativeToken || !Request.Cookies.ContainsKey("refresh_token")));

    private object ClientResponse(AuthResponse response) => IsWeb
        ? new { response.AccessToken, response.ExpiresIn, response.User }
        : response;

    private void SetRefreshTokenCookie(string refreshToken, int expiresInSeconds)
    {
        if (!IsWeb) return;
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
        if (!IsWeb) return;
        Response.Cookies.Delete("refresh_token", new CookieOptions
        {
            Path = "/api/v1/auth"
        });
    }

    private static UserSummaryDto ToUserSummary(ApplicationUser user, string role, string? ptVerificationStatus = null) =>
        new(user.Id, user.Email ?? "", user.AvatarUrl, user.FullName, role, ptVerificationStatus);

    private async Task<string?> GetPtVerificationStatusAsync(Guid userId, string role, CancellationToken ct)
    {
        if (!role.Equals("pt", StringComparison.OrdinalIgnoreCase)) return null;
        var db = HttpContext.RequestServices.GetRequiredService<FitmateDbContext>();
        var status = await db.PtProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (PtVerificationStatus?)p.VerificationStatus)
            .SingleOrDefaultAsync(ct);
        return status?.ToString().ToLowerInvariant();
    }

    private static string PreferredRole(IList<string> roles) =>
        roles.FirstOrDefault(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)) ?? roles.FirstOrDefault() ?? "member";

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
