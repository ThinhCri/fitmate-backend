using Fitmate.Domain.Payment;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class SessionPackageAllocationTests
{
    [Theory]
    [InlineData(4, 400_003)]
    [InlineData(8, 1_000_007)]
    [InlineData(4, 4)]
    public void Splits_collected_vnd_and_fee_without_rounding_drift(int count, long total)
    {
        var allocations = SessionPackageAllocation.Allocate(total, count);

        allocations.Should().HaveCount(count);
        allocations.Select(x => x.PaidVnd).Should().BeInDescendingOrder();
        allocations.Sum(x => x.PaidVnd).Should().Be(total);
        allocations.Sum(x => x.PlatformFeeVnd).Should().Be(
            (long)Math.Round(total * 0.10m, MidpointRounding.AwayFromZero));
        allocations.Sum(x => x.TrainerShareVnd).Should().Be(total - allocations.Sum(x => x.PlatformFeeVnd));
        allocations.Should().OnlyContain(x => x.PaidVnd > 0 && x.PlatformFeeVnd >= 0 &&
            x.TrainerShareVnd == x.PaidVnd - x.PlatformFeeVnd);
    }

    [Fact]
    public void Rejects_unsupported_count_and_zero_value_sessions()
    {
        Action unsupported = () => SessionPackageAllocation.Allocate(100_000, 5);
        Action tooSmall = () => SessionPackageAllocation.Allocate(3, 4);
        unsupported.Should().Throw<ArgumentOutOfRangeException>();
        tooSmall.Should().Throw<ArgumentOutOfRangeException>();
    }
}
