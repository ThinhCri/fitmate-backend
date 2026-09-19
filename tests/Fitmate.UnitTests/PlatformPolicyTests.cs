using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-151: Unit tests cho <see cref="PlatformPolicyService"/>.
/// Đảm bảo policy trả về đúng DEC-029 (read-only, 10% mặc định, validate bps).
/// </summary>
public class PlatformPolicyTests
{
    private static PlatformPolicyService NewService(PlatformPolicyOptions options)
        => new(Options.Create(options), AlwaysAllowAuthz.Instance);

    private static PlatformPolicyOptions DefaultOptions() => new()
    {
        FeeBps = 1000,
        PolicyVersion = "v1",
        EffectiveFromUtc = new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task GetCurrentAsync_ReturnsDefaults_WhenOptionsAreDefault()
    {
        // Arrange
        var svc = NewService(DefaultOptions());

        // Act
        var policy = await svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert — DEC-029 defaults
        Assert.Equal(1000, policy.PlatformFeeRateBps);
        Assert.Equal("v1", policy.PolicyVersion);
        Assert.Equal(new DateTime(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc), policy.EffectiveFrom);
        Assert.False(policy.Editable);
        Assert.Contains("10.00%", policy.DisplayHint);
        Assert.Contains("cố định", policy.DisplayHint);
    }

    [Fact]
    public async Task GetCurrentAsync_EditableIsAlwaysFalse_RegardlessOfOptions()
    {
        // Arrange — dù config có gì, release đầu KHÔNG cho phép admin sửa qua API.
        var options = DefaultOptions();
        options.FeeBps = 2500;
        options.PolicyVersion = "v2-test";
        var svc = NewService(options);

        // Act
        var policy = await svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.False(policy.Editable);
    }

    [Fact]
    public async Task GetCurrentAsync_ReflectsOverrideFeeBps_WhenNonDefault()
    {
        // Arrange — khi override env PLATFORM_FEE_BPS=500 (= 5.00%) cho staging/test.
        var options = DefaultOptions();
        options.FeeBps = 500;
        var svc = NewService(options);

        // Act
        var policy = await svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal(500, policy.PlatformFeeRateBps);
        Assert.Contains("5.00%", policy.DisplayHint);
        Assert.Contains("override", policy.DisplayHint);
    }

    [Fact]
    public async Task GetCurrentAsync_AcceptsZeroBps_ForNoFeeConfiguration()
    {
        // Arrange — phí 0% hợp lệ cho release khuyến mãi / nội bộ.
        var options = DefaultOptions();
        options.FeeBps = 0;
        var svc = NewService(options);

        // Act
        var policy = await svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal(0, policy.PlatformFeeRateBps);
        Assert.Contains("0.00%", policy.DisplayHint);
    }

    [Fact]
    public async Task GetCurrentAsync_ThrowsValidation_WhenBpsOutOfRange()
    {
        // Arrange — bps vượt 10000% phải fail (DEC-029: validate via Money.ValidateBps).
        var options = DefaultOptions();
        options.FeeBps = 15000;
        var svc = NewService(options);

        // Act + Assert — Money.ValidateBps ném ArgumentOutOfRangeException.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_ThrowsValidation_WhenBpsNegative()
    {
        // Arrange
        var options = DefaultOptions();
        options.FeeBps = -100;
        var svc = NewService(options);

        // Act + Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetCurrentAsync_AcceptsBoundaryBps_ZeroAndTenThousand()
    {
        // Arrange — boundary 0 và 10000 phải pass ValidateBps.
        foreach (var bps in new[] { 0, 10000 })
        {
            var options = DefaultOptions();
            options.FeeBps = bps;
            var svc = NewService(options);

            // Act
            var policy = await svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None);

            // Assert
            Assert.Equal(bps, policy.PlatformFeeRateBps);
        }
    }

    [Fact]
    public async Task GetCurrentAsync_PolicyVersionAndEffectiveFromPropagateVerbatim()
    {
        // Arrange — đảm bảo các trường non-default propagate đúng.
        var options = DefaultOptions();
        options.PolicyVersion = "v2.5-rollout";
        options.EffectiveFromUtc = new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
        var svc = NewService(options);

        // Act
        var policy = await svc.GetCurrentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal("v2.5-rollout", policy.PolicyVersion);
        Assert.Equal(new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc), policy.EffectiveFrom);
    }

    [Fact]
    public async Task GetCurrentAsync_OptionsPropertyDefaults_AreDocumentedInDecisions()
    {
        // Arrange — sanity check property defaults trên PlatformPolicyOptions để chống
        // tay đổi default mà không cập nhật DEC-029.
        var options = new PlatformPolicyOptions();

        // Assert
        Assert.Equal(1000, options.FeeBps);
        Assert.Equal("v1", options.PolicyVersion);
        Assert.Equal("PlatformPolicy", PlatformPolicyOptions.SectionName);
    }
}
