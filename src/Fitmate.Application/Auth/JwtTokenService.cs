using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fitmate.Application.Common;
using Microsoft.IdentityModel.Tokens;

namespace Fitmate.Application.Auth;

/// <summary>
/// Tạo + validate JWT access token (HS256).
/// Claims chuẩn:
/// - <c>sub</c> = user Guid
/// - <c>email</c> = email
/// - <c>jti</c> = unique token id (dùng để revoke nếu cần sau này)
/// - <c>role</c> = role name (Identity role claim)
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Issue access token cho user đã authenticated.</summary>
    JwtToken Issue(JwtTokenRequest request);

    /// <summary>Validate token, trả principal nếu hợp lệ + chưa hết hạn.</summary>
    ClaimsPrincipal? Validate(string token);
}

public record JwtTokenRequest(Guid UserId, string Email, string Role, string FullName);

public record JwtToken(string AccessToken, int ExpiresInSeconds);

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
            throw new InvalidOperationException("JwtOptions.SigningKey must be configured");
        _options = options;
    }

    public JwtToken Issue(JwtTokenRequest request)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddSeconds(_options.AccessTokenTtlSeconds);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, request.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, request.UserId.ToString()),
            new(ClaimTypes.Email, request.Email),
            new(ClaimTypes.Role, request.Role),
            new("full_name", request.FullName),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new JwtToken(token, _options.AccessTokenTtlSeconds);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var validation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _options.Issuer,
            ValidAudience = _options.Audience,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, validation, out _);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
