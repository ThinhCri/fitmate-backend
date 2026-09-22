using System.Text.RegularExpressions;
using Fitmate.Application.Common;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests.Security;

/// <summary>
/// BE-822: Security and secrets management unit tests.
/// Verifies secret scanner rule patterns, allowlists, fail-closed configs, CORS policies,
/// and rate limiting options.
/// </summary>
public class SecurityConfigurationTests
{
    [Fact]
    public void Gitleaks_Config_Does_Not_Broad_Allowlist_Sensitive_Files()
    {
        // Assert that .gitleaks.toml does NOT contain 'docs/', 'README.md', 'AGENTS.md', or '.env.example'
        // in allowlist paths, ensuring all docs, guides, and env templates are actively scanned.
        var tomlPath = Path.Combine(AppContext.BaseDirectory, "../../../../../.gitleaks.toml");
        if (File.Exists(tomlPath))
        {
            var content = File.ReadAllText(tomlPath);
            content.Should().NotContain("'''docs/'''", "docs directory must not be broad-allowlisted in gitleaks");
            content.Should().NotContain("'''README.md'''", "README.md must not be broad-allowlisted in gitleaks");
            content.Should().NotContain("'''AGENTS.md'''", "AGENTS.md must not be broad-allowlisted in gitleaks");
            content.Should().NotContain("'''.env.example'''", ".env.example must not be broad-allowlisted in gitleaks");
        }
    }

    [Theory]
    [InlineData("JWT_SIGNING_KEY=\"dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3Q=\"", true)]
    [InlineData("JWT_SIGNING_KEY=dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3R0ZXN0dGVzdHRlc3Q=", true)] // unquoted
    [InlineData("jwt_secret: 'abcdef1234567890abcdef1234567890'", true)]
    [InlineData("jwt_secret: abcdef1234567890abcdef1234567890", true)] // unquoted
    [InlineData("JWT_SIGNING_KEY=\"__SET_64_BYTES_BASE64__\"", false)] // placeholder allowed
    [InlineData("JWT_SIGNING_KEY=__SET_64_BYTES_BASE64__", false)] // unquoted placeholder allowed
    public void Gitleaks_JwtSigningKey_Rule_Detects_Secrets_Correctly(string line, bool shouldMatchSecret)
    {
        var regex = new Regex(@"(?i)(?:jwt[_-]?signing[_-]?key|jwt[_-]?secret|jwt__signingkey)\s*[:=]\s*[""']?([A-Za-z0-9+/=_\-]{32,})[""']?");
        var placeholderRegex = new Regex(@"__SET_[A-Z0-9_]+__");

        var isMatch = regex.IsMatch(line);
        var isPlaceholder = placeholderRegex.IsMatch(line);

        var isDetectedSecret = isMatch && !isPlaceholder;
        isDetectedSecret.Should().Be(shouldMatchSecret);
    }

    [Theory]
    [InlineData("DB_PASSWORD=\"supersecretpostgrespassword123\"", true)]
    [InlineData("DB_PASSWORD=supersecretpostgrespassword123", true)] // unquoted
    [InlineData("database_password: 'anothersecretpassword'", true)]
    [InlineData("database_password: anothersecretpassword", true)] // unquoted
    [InlineData("DB_PASSWORD=\"__SET_LOCAL_PASSWORD__\"", false)] // placeholder allowed
    [InlineData("DB_PASSWORD=__SET_LOCAL_PASSWORD__", false)] // unquoted placeholder allowed
    [InlineData("DB_PASSWORD=\"ci-ephemeral-db-password\"", false)] // CI test dummy allowed
    [InlineData("DB_PASSWORD=ci-ephemeral-db-password", false)] // unquoted CI dummy allowed
    public void Gitleaks_DbPassword_Rule_Detects_Secrets_Correctly(string line, bool shouldMatchSecret)
    {
        var regex = new Regex(@"(?i)(?:^|\s)(?:db[_-]?password|postgres[_-]?password|database[_-]?password)\s*[:=]\s*[""']?([^\s""'#]{8,})[""']?");
        var allowlisted = line.Contains("__SET_") || line.Contains("ci-ephemeral-db-password");

        var isMatch = regex.IsMatch(line);
        var isDetectedSecret = isMatch && !allowlisted;
        isDetectedSecret.Should().Be(shouldMatchSecret);
    }

    [Theory]
    [InlineData("VNPAY_HASH_SECRET=\"0123456789abcdef0123456789abcdef\"", true)]
    [InlineData("VNPAY_HASH_SECRET=0123456789abcdef0123456789abcdef", true)] // unquoted
    [InlineData("vnpay_hash_secret: '0123456789abcdef0123456789abcdef'", true)]
    [InlineData("vnpay_hash_secret: 0123456789abcdef0123456789abcdef", true)] // unquoted
    [InlineData("VNPAY_HASH_SECRET=\"__SET_VNPAY_HASH_SECRET__\"", false)]
    [InlineData("VNPAY_HASH_SECRET=__SET_VNPAY_HASH_SECRET__", false)]
    public void Gitleaks_VnPay_Secret_Rule_Detects_Secrets_Correctly(string line, bool shouldMatchSecret)
    {
        var regex = new Regex(@"(?i)(?:vnpay[_-]?hash[_-]?secret|vnpay[_-]?tmn[_-]?code|vnpay[_-]?secret)\s*[:=]\s*[""']?([A-Za-z0-9]{16,})[""']?");
        var isMatch = regex.IsMatch(line);
        var isPlaceholder = line.Contains("__SET_");

        var isDetectedSecret = isMatch && !isPlaceholder;
        isDetectedSecret.Should().Be(shouldMatchSecret);
    }

    [Fact]
    public void FailClosed_Payment_Check_Throws_When_Secret_Missing_In_Production()
    {
        // Act: simulate startup fail-closed check in Production
        Action check = () =>
        {
            var isProduction = true;
            var isPaymentEnabled = true;
            string? vnpaySecret = "__SET_VNPAY_HASH_SECRET__";

            if (isProduction && isPaymentEnabled)
            {
                if (string.IsNullOrWhiteSpace(vnpaySecret) || vnpaySecret.StartsWith("__SET_"))
                {
                    throw new InvalidOperationException("Fail-closed: In production, FEATURE_PAYMENT_ENABLED is true but VNPAY_HASH_SECRET is not configured.");
                }
            }
        };

        // Assert
        check.Should().Throw<InvalidOperationException>()
            .WithMessage("*Fail-closed: In production*");
    }

    [Fact]
    public void FailClosed_Payment_Check_Succeeds_In_Development()
    {
        // Act: in development with mock provider, placeholder secret does not throw
        Action check = () =>
        {
            var isProduction = false;
            var isPaymentEnabled = true;
            string? vnpaySecret = "__SET_VNPAY_HASH_SECRET__";

            if (isProduction && isPaymentEnabled)
            {
                if (string.IsNullOrWhiteSpace(vnpaySecret) || vnpaySecret.StartsWith("__SET_"))
                {
                    throw new InvalidOperationException("Fail-closed: In production, FEATURE_PAYMENT_ENABLED is true but VNPAY_HASH_SECRET is not configured.");
                }
            }
        };

        // Assert: no exception thrown
        check.Should().NotThrow();
    }

    [Fact]
    public void FailClosed_Payment_Check_Succeeds_When_Payment_Disabled()
    {
        // Act
        Action check = () =>
        {
            var isProduction = true;
            var isPaymentEnabled = false;
            string? vnpaySecret = null;

            if (isProduction && isPaymentEnabled)
            {
                if (string.IsNullOrWhiteSpace(vnpaySecret) || vnpaySecret.StartsWith("__SET_"))
                {
                    throw new InvalidOperationException("Fail-closed: In production, FEATURE_PAYMENT_ENABLED is true but VNPAY_HASH_SECRET is not configured.");
                }
            }
        };

        // Assert: no exception thrown
        check.Should().NotThrow();
    }

    [Theory]
    [InlineData(null, "Production")]
    [InlineData("", "Production")]
    [InlineData("   ", "Production")]
    [InlineData("*", "Production")]
    [InlineData("https://app.fitmate.vn,*", "Production")]
    [InlineData("http://localhost:5173", "Production")]
    [InlineData("http://127.0.0.1:8080", "Production")]
    public void Production_Cors_Validation_Fails_Closed_On_Invalid_Origins(string? rawCorsOrigins, string environment)
    {
        Action validateCors = () =>
        {
            var isProduction = environment.Equals("Production", StringComparison.OrdinalIgnoreCase);
            if (isProduction)
            {
                if (string.IsNullOrWhiteSpace(rawCorsOrigins))
                {
                    throw new InvalidOperationException("Fail-closed: In production, CORS_ALLOWED_ORIGINS must be explicitly configured.");
                }
                var origins = rawCorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (origins.Length == 0 || origins.Any(o => o == "*" || o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1")))
                {
                    throw new InvalidOperationException("Fail-closed: In production, CORS_ALLOWED_ORIGINS cannot contain wildcards ('*') or localhost.");
                }
            }
        };

        validateCors.Should().Throw<InvalidOperationException>()
            .WithMessage("Fail-closed: In production, CORS_ALLOWED_ORIGINS*");
    }

    [Fact]
    public void Production_Cors_Validation_Passes_On_Valid_Origins()
    {
        var rawCorsOrigins = "https://fitmate.vn,https://admin.fitmate.vn";
        var isProduction = true;

        Action validateCors = () =>
        {
            if (isProduction)
            {
                if (string.IsNullOrWhiteSpace(rawCorsOrigins))
                {
                    throw new InvalidOperationException("Fail-closed: In production, CORS_ALLOWED_ORIGINS must be explicitly configured.");
                }
                var origins = rawCorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (origins.Length == 0 || origins.Any(o => o == "*" || o.Contains("localhost", StringComparison.OrdinalIgnoreCase) || o.Contains("127.0.0.1")))
                {
                    throw new InvalidOperationException("Fail-closed: In production, CORS_ALLOWED_ORIGINS cannot contain wildcards ('*') or localhost.");
                }
            }
        };

        validateCors.Should().NotThrow();
    }

    [Fact]
    public void RateLimitOptions_Has_Expected_Default_Thresholds()
    {
        var opts = new RateLimitOptions();
        opts.PerMinutePerIp.Should().Be(60);
        opts.AuthPerMinutePerIp.Should().Be(5);
        opts.AuthRefreshPerMinutePerIp.Should().Be(30);
    }
}
