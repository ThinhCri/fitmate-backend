using System.Net;
using System.Net.Http.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fitmate.IntegrationTests;

public sealed class AiUsageIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public AiUsageIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Usage_status_requires_authentication()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/me/ai-usage");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Usage_status_reports_disabled_enforcement_without_claiming_plus()
    {
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: Guid.NewGuid());
        var response = await client.GetAsync("/api/v1/me/ai-usage");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<AiUsageView>();
        status.Should().NotBeNull();
        status!.Plan.Should().Be("free");
        status.EnforcementEnabled.Should().BeFalse();
        status.Chat.Used.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_reservations_do_not_exceed_daily_limit_and_failures_release()
    {
        var userId = Guid.NewGuid();
        var options = Options.Create(new AiUsageOptions { Enabled = true, FreeChatPerDay = 1 });
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        var first = new EfAiUsageService(scope1.ServiceProvider.GetRequiredService<FitmateDbContext>(), options,
            scope1.ServiceProvider.GetRequiredService<IAiSubscriptionService>());
        var second = new EfAiUsageService(scope2.ServiceProvider.GetRequiredService<FitmateDbContext>(), options,
            scope2.ServiceProvider.GetRequiredService<IAiSubscriptionService>());

        var results = await Task.WhenAll(
            TryReserve(first, userId), TryReserve(second, userId));
        results.Count(x => x is not null).Should().Be(1);
        var winner = results.Single(x => x is not null)!;

        await (results[0] is not null ? first : second).ReleaseAsync(winner, CancellationToken.None);
        var retry = await first.ReserveAsync(userId, "chat", CancellationToken.None);
        await first.CommitAsync(retry, CancellationToken.None);
        var status = await first.GetAsync(userId, CancellationToken.None);
        status.Chat.Used.Should().Be(1);
        status.Chat.Remaining.Should().Be(0);
        await FluentActions.Invoking(() => second.ReserveAsync(userId, "chat", CancellationToken.None))
            .Should().ThrowAsync<AiQuotaExceededException>();
    }

    private static async Task<AiUsageReservation?> TryReserve(IAiUsageService service, Guid userId)
    {
        try { return await service.ReserveAsync(userId, "chat", CancellationToken.None); }
        catch (AiQuotaExceededException) { return null; }
    }
}
