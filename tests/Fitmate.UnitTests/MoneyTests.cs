using Fitmate.Application.Common;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 1000)]
    [InlineData(1000.4, 1000)]
    [InlineData(1000.5, 1001)] // half-up
    [InlineData(1000.6, 1001)]
    [InlineData(999.5, 1000)]
    public void RoundVnd_HalfUp(double input, long expected)
    {
        Money.RoundVnd((decimal)input).Should().Be(expected);
    }

    [Fact]
    public void SplitFee_500000Vnd_At10Percent_PlatformFee50000_TrainerShare450000()
    {
        var (fee, share) = Money.SplitFee(actualPaidVnd: 500_000, feeRate: 0.10m);
        fee.Should().Be(50_000);
        share.Should().Be(450_000);
    }

    [Fact]
    public void SplitFee_1001Vnd_At10Percent_HalfUp_Fee101_Share900()
    {
        var (fee, share) = Money.SplitFee(actualPaidVnd: 1001, feeRate: 0.10m);
        fee.Should().Be(100);
        share.Should().Be(901);
    }

    [Theory]
    [InlineData(-1, 0.1)]
    [InlineData(0, -0.1)]
    [InlineData(0, 1.5)]
    public void SplitFee_InvalidArgs_Throws(long paid, double rate)
    {
        var act = () => Money.SplitFee(paid, (decimal)rate);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ApplyRefund_100000Paid_50000Already_30000More_Succeeds()
    {
        var newTotal = Money.ApplyRefund(totalPaidVnd: 100_000, alreadyRefundedVnd: 50_000, refundThisTimeVnd: 30_000);
        newTotal.Should().Be(80_000);
    }

    [Fact]
    public void ApplyRefund_CapExceeded_Throws()
    {
        var act = () => Money.ApplyRefund(totalPaidVnd: 100_000, alreadyRefundedVnd: 80_000, refundThisTimeVnd: 30_000);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Refund cap*");
    }

    [Fact]
    public void ApplyRefund_ZeroOrNegative_Throws()
    {
        var act = () => Money.ApplyRefund(100_000, 0, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ===================== DEC-029: Bps split =====================

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void SplitFeeBps_InvalidBps_Throws(int bps)
    {
        var act = () => Money.SplitFeeBps(100_000, bps);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SplitFeeBps_500000_At1000Bps_Fee50000_Share450000()
    {
        var (fee, share) = Money.SplitFeeBps(500_000, platformFeeRateBps: 1000);
        fee.Should().Be(50_000);
        share.Should().Be(450_000);
    }

    [Fact]
    public void SplitFeeBps_1001_At1000Bps_Fee100_Share901()
    {
        // 1001 * 1000 / 10000 = 100.1 → 100 (AwayFromZero half-up); share = 901.
        var (fee, share) = Money.SplitFeeBps(1001, 1000);
        fee.Should().Be(100);
        share.Should().Be(901);
    }

    [Fact]
    public void SplitFeeBps_1050_At1000Bps_HalfUp_Fee105_Share945()
    {
        // 1050 * 1000/10000 = 105.0 → 105 (no rounding needed)
        var (fee, share) = Money.SplitFeeBps(1050, 1000);
        fee.Should().Be(105);
        share.Should().Be(945);
    }

    [Fact]
    public void SplitFeeBps_ZeroPaid_ZeroFee()
    {
        var (fee, share) = Money.SplitFeeBps(0, 1000);
        fee.Should().Be(0);
        share.Should().Be(0);
    }

    [Fact]
    public void SplitFeeBps_ZeroBps_ZeroFee_AllShare()
    {
        var (fee, share) = Money.SplitFeeBps(100_000, 0);
        fee.Should().Be(0);
        share.Should().Be(100_000);
    }

    [Fact]
    public void SplitFeeBps_FullBps_AllFee_ZeroShare()
    {
        var (fee, share) = Money.SplitFeeBps(100_000, 10_000);
        fee.Should().Be(100_000);
        share.Should().Be(0);
    }

    [Theory]
    [InlineData(0, "0.00%")]
    [InlineData(1000, "10.00%")]
    [InlineData(100, "1.00%")]
    [InlineData(1234, "12.34%")]
    [InlineData(10_000, "100.00%")]
    public void FormatPercent_Bps_ToInvariantString(int bps, string expected)
    {
        Money.FormatPercent(bps).Should().Be(expected);
    }

    // ===================== DEC-008: refund allocation =====================

    [Fact]
    public void AllocateRefund_Proportional_SumEqualsRefund()
    {
        // paid: fee 10000, share 90000. Refund 30000.
        var (refundFee, refundShare) = Money.AllocateRefund(paidFeeVnd: 10_000, paidShareVnd: 90_000, refundThisTimeVnd: 30_000);
        // 30000 * 10000/100000 = 3000, share = 27000.
        refundFee.Should().Be(3_000);
        refundShare.Should().Be(27_000);
        (refundFee + refundShare).Should().Be(30_000);
    }

    [Fact]
    public void AllocateRefund_FullPaid_RefundEverything()
    {
        var (refundFee, refundShare) = Money.AllocateRefund(paidFeeVnd: 10_000, paidShareVnd: 90_000, refundThisTimeVnd: 100_000);
        refundFee.Should().Be(10_000);
        refundShare.Should().Be(90_000);
        (refundFee + refundShare).Should().Be(100_000);
    }

    [Fact]
    public void AllocateRefund_ZeroRefund_ZeroBoth()
    {
        var (refundFee, refundShare) = Money.AllocateRefund(10_000, 90_000, 0);
        refundFee.Should().Be(0);
        refundShare.Should().Be(0);
    }

    [Fact]
    public void AllocateRefund_ZeroPaid_ZeroRefund_OK()
    {
        var (refundFee, refundShare) = Money.AllocateRefund(0, 0, 0);
        refundFee.Should().Be(0);
        refundShare.Should().Be(0);
    }

    [Fact]
    public void AllocateRefund_ZeroPaid_NonZeroRefund_Throws()
    {
        var act = () => Money.AllocateRefund(0, 0, 1000);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AllocateRefund_HalfUp_RoundsFeeUp()
    {
        // paid 1000: fee 100, share 900. Refund 333.
        // 333 * 100/1000 = 33.3 → 33 (AwayFromZero). share = 300.
        var (refundFee, refundShare) = Money.AllocateRefund(100, 900, 333);
        refundFee.Should().Be(33);
        refundShare.Should().Be(300);
        (refundFee + refundShare).Should().Be(333);
    }

    [Fact]
    public void AllocateRefund_NegativeArgs_Throws()
    {
        var act1 = () => Money.AllocateRefund(-1, 0, 0);
        act1.Should().Throw<ArgumentOutOfRangeException>();
        var act2 = () => Money.AllocateRefund(0, -1, 0);
        act2.Should().Throw<ArgumentOutOfRangeException>();
        var act3 = () => Money.AllocateRefund(0, 0, -1);
        act3.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AllocateRefund_Cumulative_TwoRefunds_TotalLuyKe()
    {
        // paid: fee 10000, share 90000. 2 refunds: 30000 + 20000 = 50000.
        var (fee1, share1) = Money.AllocateRefund(10_000, 90_000, 30_000);
        var (fee2, share2) = Money.AllocateRefund(10_000, 90_000, 20_000);

        var totalFee = fee1 + fee2;
        var totalShare = share1 + share2;
        totalFee.Should().Be(5_000); // 50000 * 10000/100000 = 5000
        totalShare.Should().Be(45_000);
        (totalFee + totalShare).Should().Be(50_000);
    }

    [Fact]
    public void ApplyCumulativeRefund_DelegatesAndCaps()
    {
        Money.ApplyCumulativeRefund(100_000, 0, 30_000).Should().Be(30_000);
        Money.ApplyCumulativeRefund(100_000, 30_000, 70_000).Should().Be(100_000);

        var act = () => Money.ApplyCumulativeRefund(100_000, 80_000, 30_000);
        act.Should().Throw<InvalidOperationException>();
    }

    // ===================== DEC-029: snapshot =====================

    [Fact]
    public void BuildSnapshot_NormalCase_SumHolds()
    {
        var snap = Money.BuildSnapshot(priceVnd: 500_000, discountVnd: 0, platformFeeRateBps: 1000, policyVersion: "v1");
        snap.PriceVnd.Should().Be(500_000);
        snap.DiscountVnd.Should().Be(0);
        snap.ActualPaidVnd.Should().Be(500_000);
        snap.PlatformFeeRateBps.Should().Be(1000);
        snap.PlatformFeeVnd.Should().Be(50_000);
        snap.TrainerShareVnd.Should().Be(450_000);
        snap.PolicyVersion.Should().Be("v1");
        snap.SumHolds().Should().BeTrue();
    }

    [Fact]
    public void BuildSnapshot_WithDiscount_SumHolds()
    {
        var snap = Money.BuildSnapshot(priceVnd: 500_000, discountVnd: 50_000, platformFeeRateBps: 1000, policyVersion: "v1");
        snap.ActualPaidVnd.Should().Be(450_000);
        snap.PlatformFeeVnd.Should().Be(45_000);
        snap.TrainerShareVnd.Should().Be(405_000);
        snap.SumHolds().Should().BeTrue();
    }

    [Fact]
    public void BuildSnapshot_FullPrice_AllFee()
    {
        var snap = Money.BuildSnapshot(100_000, 0, 10_000, "v1");
        snap.PlatformFeeVnd.Should().Be(100_000);
        snap.TrainerShareVnd.Should().Be(0);
        snap.SumHolds().Should().BeTrue();
    }

    [Fact]
    public void BuildSnapshot_DiscountExceedsPrice_Throws()
    {
        var act = () => Money.BuildSnapshot(100_000, 100_001, 1000, "v1");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildSnapshot_EmptyPolicyVersion_Throws()
    {
        var act = () => Money.BuildSnapshot(100_000, 0, 1000, "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildSnapshot_InvalidBps_Throws()
    {
        var act = () => Money.BuildSnapshot(100_000, 0, 10_001, "v1");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildSnapshot_NegativePrice_Throws()
    {
        var act = () => Money.BuildSnapshot(-1, 0, 1000, "v1");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
