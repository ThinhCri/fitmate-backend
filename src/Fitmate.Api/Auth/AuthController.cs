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
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var validation = await _refreshValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(ValidationProblem(validation));

        var rotated = await _refresh.RotateAsync(request.RefreshToken, ct);
        if (rotated is null)
            return MakeUnauthorized("Invalid or expired refresh token.");

        // Resolve userId from the Authorization header claims (caller sends access token too).
        var userId = ResolveUserIdFromToken();
        if (userId is null)
            return MakeUnauthorized("Invalid refresh token.");

        var user = await _users.FindByIdAsync(userId.Value.ToString());
        if (user is null)
            return MakeUnauthorized("Invalid refresh token.");

        var role = (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "member";
        var access = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email ?? "", role, user.FullName));

        return Ok(new AuthResponse(
            access.AccessToken,
            rotated.RawToken,
            access.ExpiresInSeconds,
            ToUserSummary(user, role)));
    }

    [HttpPost("logout")]
    [Authorize]
    public Task<IActionResult> Logout(CancellationToken ct)
    {
        // Revoke refresh token via body optional; MVP trả 204 ngay.
        // BE-070: parse body + revoke cụ thể.
        return Task.FromResult<IActionResult>(NoContent());
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, string role, string? deviceLabel, CancellationToken ct)
    {
        var access = _jwt.Issue(new JwtTokenRequest(user.Id, user.Email ?? "", role, user.FullName));
        var refresh = await _refresh.IssueAsync(user.Id, deviceLabel, ct);
        return new AuthResponse(
            access.AccessToken,
            refresh.RawToken,
            access.ExpiresInSeconds,
            ToUserSummary(user, role));
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
