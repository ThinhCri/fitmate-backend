using Fitmate.Application.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-380: JwtOptionsLoader — load + validate JwtOptions.
/// Tests: alias resolution, conflict detection, missing key, key too short, no default fallback.
/// </summary>
public class JwtOptionsLoaderTests
{
    // Valid key: >= 32 bytes UTF-8. Base64 of "aaaa...aaaa" (64 a's) is >= 64 chars.
    private const string ValidKey = "YWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFh"; // 64 a's base64

    private static IConfiguration ConfigWith(params (string key, string? value)[] entries)
    {
        var dict = entries
            .Where(e => e.value is not null)
            .ToDictionary(e => e.key, e => e.value!);
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict!)
            .Build();
    }

    [Fact]
    public void Load_WithOnlyCanonicalEnv_ReturnsKey()
    {
        var config = ConfigWith(
            (JwtOptionsLoader.CanonicalEnvKey, ValidKey));

        var options = JwtOptionsLoader.Load(config);

        options.SigningKey.Should().Be(ValidKey);
    }

    [Fact]
    public void Load_WithOnlyAliasEnv_ReturnsKey()
    {
        var config = ConfigWith(
            (JwtOptionsLoader.AliasEnvKey, ValidKey));

        var options = JwtOptionsLoader.Load(config);

        options.SigningKey.Should().Be(ValidKey);
    }

    [Fact]
    public void Load_WithOnlySectionKey_ReturnsKey()
    {
        var config = ConfigWith(
            ("Jwt:SigningKey", ValidKey));

        var options = JwtOptionsLoader.Load(config);

        options.SigningKey.Should().Be(ValidKey);
    }

    [Fact]
    public void Load_WithCanonicalAndAliasEqual_ReturnsKey()
    {
        var config = ConfigWith(
            (JwtOptionsLoader.CanonicalEnvKey, ValidKey),
            (JwtOptionsLoader.AliasEnvKey, ValidKey));

        var options = JwtOptionsLoader.Load(config);

        options.SigningKey.Should().Be(ValidKey);
    }

    [Fact]
    public void Load_WithCanonicalAndAliasDifferent_ThrowsWithClearMessage()
    {
        var config = ConfigWith(
            (JwtOptionsLoader.CanonicalEnvKey, ValidKey),
            (JwtOptionsLoader.AliasEnvKey, "YmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJiYmJi")); // different key

        var act = () => JwtOptionsLoader.Load(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*conflicting*");
    }

    [Fact]
    public void Load_WithAllThreeSourcesEqual_ReturnsKey()
    {
        var config = ConfigWith(
            (JwtOptionsLoader.CanonicalEnvKey, ValidKey),
            (JwtOptionsLoader.AliasEnvKey, ValidKey),
            ("Jwt:SigningKey", ValidKey));

        var options = JwtOptionsLoader.Load(config);

        options.SigningKey.Should().Be(ValidKey);
    }

    [Fact]
    public void Load_WithNoKey_ThrowsMissingKeyMessage()
    {
        var config = ConfigWith(); // empty

        var act = () => JwtOptionsLoader.Load(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT_SIGNING_KEY*");
    }

    [Theory]
    [InlineData(31)] // just below minimum
    [InlineData(16)] // ASP.NET minimum for HmacSha256
    [InlineData(1)]  // arbitrary short
    public void Load_WithKeyTooShort_ThrowsMinimumBytesMessage(int byteLength)
    {
        var shortKey = new string('a', byteLength);
        var config = ConfigWith(
            (JwtOptionsLoader.CanonicalEnvKey, shortKey));

        var act = () => JwtOptionsLoader.Load(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*")
            .And.Message.Should().Contain(byteLength.ToString());
    }

    [Fact]
    public void Load_WithKeyExactly32Bytes_ReturnsKey()
    {
        var key32 = new string('x', 32); // exactly 32 UTF-8 bytes
        var config = ConfigWith(
            (JwtOptionsLoader.CanonicalEnvKey, key32));

        var options = JwtOptionsLoader.Load(config);

        options.SigningKey.Should().Be(key32);
    }

    [Fact]
    public void Load_WithNullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => JwtOptionsLoader.Load(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Load_KeepsIssuerAudienceFromSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { JwtOptionsLoader.CanonicalEnvKey, ValidKey },
                { "Jwt:Issuer", "custom-issuer" },
                { "Jwt:Audience", "custom-audience" },
                { "Jwt:AccessTokenTtlSeconds", "600" }
            }!)
            .Build();

        var options = JwtOptionsLoader.Load(config);

        options.Issuer.Should().Be("custom-issuer");
        options.Audience.Should().Be("custom-audience");
        options.AccessTokenTtlSeconds.Should().Be(600);
    }
}
