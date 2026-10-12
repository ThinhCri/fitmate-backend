using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class AdminBusinessSummaryServiceTests
{
    [Fact]
    public async Task Summary_requires_admin_role()
    {
        var service = new AdminBusinessSummaryService(new Store(), new RoleRequireAuthz("Member"));
        var act = () => service.GetAsync(Guid.NewGuid(), "30d", default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Summary_rejects_unknown_range()
    {
        var service = new AdminBusinessSummaryService(new Store(), AlwaysAllowAuthz.Instance);
        var act = () => service.GetAsync(Guid.NewGuid(), "yesterday", default);
        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Summary_uses_utc_half_open_interval()
    {
        var store = new Store();
        var service = new AdminBusinessSummaryService(store, AlwaysAllowAuthz.Instance);
        var result = await service.GetAsync(Guid.NewGuid(), "7d", default);
        result.Range.Should().Be("7d");
        (result.ToUtc - result.FromUtc).Should().Be(TimeSpan.FromDays(7));
        result.FromUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    private sealed class Store : IAdminBusinessSummaryStore
    {
        public Task<BusinessSummary> GetAsync(string range, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
            => Task.FromResult(new BusinessSummary(range, fromUtc, toUtc, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }
}
