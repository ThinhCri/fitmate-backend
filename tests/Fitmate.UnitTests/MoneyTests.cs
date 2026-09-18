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
}
