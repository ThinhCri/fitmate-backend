using Fitmate.Application.Payments;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class RuntimeProviderBoundaryTests
{
    [Fact]
    public void FakePaymentProvider_IsCompiledOnlyIntoTests()
    {
        typeof(PaymentService).Assembly.GetType("Fitmate.Application.Payments.StubPaymentProvider").Should().BeNull();
        typeof(StubPaymentProvider).Assembly.Equals(typeof(RuntimeProviderBoundaryTests).Assembly).Should().BeTrue();
        new StubPaymentProvider().IsRealProvider.Should().BeFalse();
    }

    [Fact]
    public void RuntimeAssemblies_ContainNoMockOrStubProviders()
    {
        var assemblies = new[] { typeof(PaymentService).Assembly, typeof(Fitmate.Infrastructure.Notifications.FcmPushProvider).Assembly, typeof(Fitmate.Api.Capabilities.SystemCapabilitiesController).Assembly };
        foreach (var assembly in assemblies)
            assembly.GetTypes().Where(t => t.Name.Contains("StubProvider", StringComparison.Ordinal) || t.Name.Contains("MockProvider", StringComparison.Ordinal) || t.Name == "StubPaymentProvider").Should().BeEmpty();
    }
}
