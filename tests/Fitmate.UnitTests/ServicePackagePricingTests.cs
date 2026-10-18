using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class ServicePackagePricingTests
{
    [Fact]
    public void PackagePricesAreOptInAndCannotExceedSeparateSessions()
    {
        var service = Service.Create(Guid.NewGuid(), "Huấn luyện cá nhân", null, 200_000, 60);
        service.Package4PriceVnd.Should().Be(0);
        service.Package8PriceVnd.Should().Be(0);

        service.SetPackagePrices(720_000, 1_350_000);
        service.Package4PriceVnd.Should().Be(720_000);
        service.Package8PriceVnd.Should().Be(1_350_000);

        Action overpriced = () => service.SetPackagePrices(800_001, null);
        overpriced.Should().Throw<ArgumentException>();
        service.Package4PriceVnd.Should().Be(720_000);

        service.SetPackagePrices(0, null);
        service.Package4PriceVnd.Should().Be(0);
        service.Package8PriceVnd.Should().Be(1_350_000);
    }
}
