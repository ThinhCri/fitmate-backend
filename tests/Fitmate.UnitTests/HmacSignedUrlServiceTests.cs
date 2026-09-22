using System.Reflection;
using Fitmate.Application.Storage;
using Fitmate.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-420: HMAC-SHA256 presigned URL generator/validator.
/// Round-trip, expiry, tampered signature, and clamped TTL behaviour.
/// </summary>
public class HmacSignedUrlServiceTests
{
    private static HmacSignedUrlService NewService(int ttlMin = 15, string? secret = null)
        => new(Options.Create(new StorageOptions
        {
            BaseUrl = "http://localhost:5116/uploads",
            SignedUrlSecret = secret ?? "this-is-a-32-char-test-secret-XXX",
            SignedUrlTtlMinutes = ttlMin
        }));

    [Fact]
    public void CreatePresignedReadUrl_IncludesExpiryAndSig()
    {
        var svc = NewService();
        var url = svc.CreatePresignedReadUrl("avatars/u1.jpg", TimeSpan.FromMinutes(10));

        url.AbsoluteUri.Should().StartWith("http://localhost:5116/uploads/avatars/u1.jpg");
        url.Query.Should().Contain("expires=");
        url.Query.Should().Contain("sig=");
    }

    [Fact]
    public void RoundTrip_ValidateExtractsPath()
    {
        var svc = NewService();
        var url = svc.CreatePresignedReadUrl("certificates/c1.png", TimeSpan.FromMinutes(5));

        var extracted = svc.TryValidateAndExtractPath(url.PathAndQuery);

        extracted.Should().Be("certificates/c1.png");
    }

    [Fact]
    public void TryValidate_RejectsExpiredUrl_WithMatchingSig()
    {
        // Craft an URL with a matching signature but expired timestamp by computing
        // the signature for a known past expires value via reflection.
        var svc = NewService(ttlMin: 15);
        var pastExpires = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();

        var method = typeof(HmacSignedUrlService).GetMethod(
            "ComputeSignature",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var sig = (string)method!.Invoke(svc, new object[] { "avatars/u1.jpg", pastExpires })!;

        var pathAndQuery = $"/uploads/avatars/u1.jpg?expires={pastExpires}&sig={sig}";
        svc.TryValidateAndExtractPath(pathAndQuery).Should().BeNull();
    }

    [Fact]
    public void TryValidate_RejectsTamperedSignature()
    {
        var svc = NewService();
        var url = svc.CreatePresignedReadUrl("avatars/u1.jpg", TimeSpan.FromMinutes(5));

        // Tamper with the signature.
        var tampered = url.PathAndQuery.Replace("sig=", "sig=AAAA");

        var extracted = svc.TryValidateAndExtractPath(tampered);
        extracted.Should().BeNull();
    }

    [Fact]
    public void TryValidate_RejectsMissingExpiresOrSig()
    {
        var svc = NewService();

        svc.TryValidateAndExtractPath("/uploads/avatars/u1.jpg").Should().BeNull();
        svc.TryValidateAndExtractPath("/uploads/avatars/u1.jpg?sig=only").Should().BeNull();
        svc.TryValidateAndExtractPath("/uploads/avatars/u1.jpg?expires=123").Should().BeNull();
    }

    [Fact]
    public void CreatePresignedReadUrl_ClampsTtlToConfiguredMax()
    {
        var svc = NewService(ttlMin: 10);
        var url = svc.CreatePresignedReadUrl("posts/x.jpg", TimeSpan.FromHours(2));

        // expiry ≈ now + 10 min (clamped), NOT 2 hours
        var expiresIndex = url.Query.IndexOf("expires=", StringComparison.Ordinal) + "expires=".Length;
        var ampersand = url.Query.IndexOf('&', expiresIndex);
        var expiresStr = url.Query[expiresIndex..ampersand];
        var expiresUnix = long.Parse(expiresStr);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var delta = expiresUnix - now;
        delta.Should().BeLessThan(11 * 60); // < 11 min
        delta.Should().BeGreaterThan(9 * 60); // > 9 min
    }
}
