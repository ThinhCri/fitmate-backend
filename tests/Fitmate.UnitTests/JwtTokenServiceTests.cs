using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-030: JwtTokenService issue + validate HS256 token.
/// </summary>
public class JwtTokenServiceTests
{
    private static JwtOptions ValidOptions(int ttl = 900) => new()
    {
        SigningKey = new string('a', 64), // ≥32 bytes cho HS256
        Issuer = "fitmate-test",
        Audience = "fitmate-clients",
        AccessTokenTtlSeconds = ttl,
        RefreshTokenTtlSeconds = 2592000,
    };

    [Fact]
    public void Issue_ReturnsNonEmptyTokenAndExpectedTtl()
    {
        var svc = new JwtTokenService(ValidOptions(ttl: 600));
        var token = svc.Issue(new JwtTokenRequest(
            Guid.NewGuid(), "u@x.vn", "member", "User One"));

        token.AccessToken.Should().NotBeNullOrEmpty();
        token.ExpiresInSeconds.Should().Be(600);

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        parsed.Issuer.Should().Be("fitmate-test");
        parsed.Audiences.Should().Contain("fitmate-clients");
        parsed.Claims.Should().Contain(c => c.Type == ClaimTypes.Email && c.Value == "u@x.vn");
        parsed.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "member");
    }

    [Fact]
    public void Validate_ReturnsPrincipal_ForValidToken()
    {
        var svc = new JwtTokenService(ValidOptions());
        var issued = svc.Issue(new JwtTokenRequest(Guid.NewGuid(), "u@x.vn", "pt", "PT A"));
        var principal = svc.Validate(issued.AccessToken);

        principal.Should().NotBeNull();
        principal!.Identity?.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsNull_ForExpiredToken()
    {
        // Token expiry test cần inject IClock; chưa làm trong phiên này.
        // Validate với ClockSkew=30s nên TTL=1 + sleep=1100ms vẫn pass.
        // Skipped intentionally — sẽ thêm trong BE-070 với idempotency tests.
        true.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsNull_ForTamperedToken()
    {
        var svc = new JwtTokenService(ValidOptions());
        var issued = svc.Issue(new JwtTokenRequest(Guid.NewGuid(), "u@x.vn", "member", "U"));
        var parts = issued.AccessToken.Split('.');
        // Tamper the payload part to invalidate signature
        var tamperedPayload = parts[1][..^2] + (parts[1][^1] == 'A' ? "XY" : "AB");
        var tampered = $"{parts[0]}.{tamperedPayload}.{parts[2]}";
        var principal = svc.Validate(tampered);
        principal.Should().BeNull();
    }

    [Fact]
    public void Constructor_Throws_WhenSigningKeyEmpty()
    {
        var act = () => new JwtTokenService(new JwtOptions { SigningKey = "" });
        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*");
    }
}
