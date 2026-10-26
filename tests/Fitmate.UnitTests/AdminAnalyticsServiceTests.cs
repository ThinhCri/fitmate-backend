using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class AdminAnalyticsServiceTests
{
    [Fact]
    public async Task Non_admin_cannot_read_provider_or_metrics()
    {
        var provider = new Provider();
        var service = new AdminAnalyticsService(provider, new RoleRequireAuthz("Member"));
        var action = () => service.GetAsync(Guid.NewGuid(), "7d", default, "landing");
        await action.Should().ThrowAsync<ForbiddenException>();
        provider.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("7d", "invalid")]
    [InlineData("forever", "app")]
    public async Task Rejects_unsupported_queries_before_calling_provider(string range, string source)
    {
        var provider = new Provider();
        var service = new AdminAnalyticsService(provider, AlwaysAllowAuthz.Instance);
        var action = () => service.GetAsync(Guid.NewGuid(), range, default, source);
        await action.Should().ThrowAsync<DomainException>();
        provider.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Unconfigured_source_does_not_return_fake_zeros()
    {
        var result = await new AdminAnalyticsService(new Provider(), AlwaysAllowAuthz.Instance)
            .GetAsync(Guid.NewGuid(), "7d", default, "landing");
        result.Source.Should().Be("landing");
        result.Configured.Should().BeFalse();
        result.ActiveUsers.Should().BeNull();
    }

    private sealed class Provider : IGa4ReportProvider
    {
        public int Reads { get; private set; }
        public bool IsConfigured { get { Reads++; return false; } }
        public Task<AnalyticsOverview> GetAsync(string range, int days, CancellationToken ct, string source = "all") =>
            throw new InvalidOperationException("Provider must not be called in these cases.");
    }
}
