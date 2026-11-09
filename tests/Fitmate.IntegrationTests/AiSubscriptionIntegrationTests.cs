using Fitmate.Application.Ai;
using System.Net;
using System.Net.Http.Json;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fitmate.IntegrationTests;

public sealed class AiSubscriptionIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public AiSubscriptionIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Subscription_api_requires_auth_and_fails_closed_without_play_configuration()
    {
        var anonymous = await _factory.CreateClient().GetAsync("/api/v1/me/ai-subscription");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: Guid.NewGuid());
        var status = await client.GetFromJsonAsync<AiSubscriptionView>("/api/v1/me/ai-subscription");
        status.Should().NotBeNull();
        status!.BillingAvailable.Should().BeFalse();
        status.RestoreAvailable.Should().BeFalse();
        status.Plan.Should().Be("free");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/ai-subscription/verify")
        {
            Content = JsonContent.Create(new { purchase_token = "test-token" })
        };
        request.Headers.Add("Idempotency-Key", "test_subscription_disabled_123");
        var verify = await client.SendAsync(request);
        verify.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Verified_purchase_is_bound_to_one_account_and_grants_only_valid_states()
    {
        using var scope = _factory.Services.CreateScope();
        var google = new FakeVerifier();
        var service = new EfAiSubscriptionService(scope.ServiceProvider.GetRequiredService<FitmateDbContext>(),
            google, Options.Create(new GooglePlayBillingOptions { Enabled = true }),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var token = "test-purchase-" + Guid.NewGuid().ToString("N");
        google.Result = new VerifiedGoogleSubscription("fitmate_ai_plus", "monthly",
            "SUBSCRIPTION_STATE_ACTIVE", DateTime.UtcNow.AddDays(20), null, false);
        (await service.VerifyAsync(owner, token, CancellationToken.None)).Plan.Should().Be("plus");
        var ownerStatus = await service.GetAsync(owner, CancellationToken.None);
        ownerStatus.RestoreAvailable.Should().BeTrue();
        ownerStatus.BillingAvailable.Should().BeFalse();
        google.AcknowledgeCount.Should().Be(1);
        (await service.VerifyAsync(owner, token, CancellationToken.None)).Plan.Should().Be("plus");
        await FluentActions.Invoking(() => service.VerifyAsync(other, token, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().Where(x => x.HttpStatus == 409);
        google.AcknowledgeCount.Should().Be(2);

        google.Result = google.Result with { State = "SUBSCRIPTION_STATE_ON_HOLD" };
        (await service.VerifyAsync(owner, token, CancellationToken.None)).Plan.Should().Be("free");
        google.Result = google.Result with { State = "SUBSCRIPTION_STATE_CANCELED" };
        (await service.VerifyAsync(owner, token, CancellationToken.None)).Plan.Should().Be("free");
        google.Result = google.Result with { ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) };
        (await service.VerifyAsync(owner, token, CancellationToken.None)).Plan.Should().Be("free");
    }

    [Fact]
    public async Task Recently_checked_expired_token_cannot_hide_active_purchase_or_transfer_it()
    {
        using var scope = _factory.Services.CreateScope();
        var google = new FakeVerifier();
        var service = new EfAiSubscriptionService(scope.ServiceProvider.GetRequiredService<FitmateDbContext>(),
            google, Options.Create(new GooglePlayBillingOptions { Enabled = true }),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var active = "active-test-" + Guid.NewGuid(); var expired = "expired-test-" + Guid.NewGuid();
        google.Result = new("fitmate_ai_plus", "monthly", "SUBSCRIPTION_STATE_ACTIVE", DateTime.UtcNow.AddDays(20), null, true);
        await service.VerifyAsync(owner, active, default);
        google.Result = google.Result with { State = "SUBSCRIPTION_STATE_EXPIRED", ExpiresAtUtc = DateTime.UtcNow.AddDays(-1) };
        await service.VerifyAsync(owner, expired, default);
        (await service.GetAsync(owner, default)).Plan.Should().Be("plus");
        (await service.GetAsync(other, default)).Plan.Should().Be("free");
        google.Result = google.Result with { State = "SUBSCRIPTION_STATE_ACTIVE", ExpiresAtUtc = DateTime.UtcNow.AddDays(20) };
        await FluentActions.Invoking(() => service.VerifyAsync(other, active, default))
            .Should().ThrowAsync<DomainException>().Where(x => x.Code == "AI_SUBSCRIPTION_BOUND_TO_ACCOUNT");
        (await service.GetAsync(owner, default)).Plan.Should().Be("plus");
        (await service.GetAsync(other, default)).Plan.Should().Be("free");
    }
    [Fact]
    public async Task Google_account_identifier_must_match_buyer_before_acknowledgement()
    {
        using var scope = _factory.Services.CreateScope();
        var google = new FakeVerifier();
        var service = new EfAiSubscriptionService(scope.ServiceProvider.GetRequiredService<FitmateDbContext>(),
            google, Options.Create(new GooglePlayBillingOptions { Enabled = true }),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var binding = (await service.GetAsync(owner, default)).BillingAccountId;
        binding.Should().HaveLength(64);
        google.Result = new("fitmate_ai_plus", "monthly", "SUBSCRIPTION_STATE_ACTIVE", DateTime.UtcNow.AddDays(20), null, false, false, null, binding);
        var token = "bound-test-" + Guid.NewGuid();
        await FluentActions.Invoking(() => service.VerifyAsync(other, token, default))
            .Should().ThrowAsync<DomainException>().Where(x => x.Code == "AI_SUBSCRIPTION_BOUND_TO_ACCOUNT");
        google.AcknowledgeCount.Should().Be(0);
        (await service.VerifyAsync(owner, token, default)).Plan.Should().Be("plus");
        google.AcknowledgeCount.Should().Be(1);
        (await service.GetAsync(other, default)).Plan.Should().Be("free");
    }
    private sealed class FakeVerifier : IGooglePlaySubscriptionVerifier
    {
        public VerifiedGoogleSubscription Result { get; set; } = null!;
        public int AcknowledgeCount { get; private set; }
        public Task<VerifiedGoogleSubscription> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(Result);
        public Task AcknowledgeAsync(string token, CancellationToken ct)
        {
            AcknowledgeCount++;
            return Task.CompletedTask;
        }
    }
}
