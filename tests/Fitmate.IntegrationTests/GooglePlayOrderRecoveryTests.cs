using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Ai;
using Fitmate.Domain.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Fitmate.IntegrationTests;

public sealed class GooglePlayOrderRecoveryTests(FitmateTestAppFactory factory) : IClassFixture<FitmateTestAppFactory>
{
    private sealed class Google : IGooglePlaySubscriptionVerifier, IGooglePlayOrderReader, IGooglePlayOrderDiscovery
    {
        public string Token { get; } = Guid.NewGuid().ToString("N");
        public string Previous { get; } = Guid.NewGuid().ToString("N");
        public string Order { get; } = "GPA." + Guid.NewGuid().ToString("N");
        public bool Test { get; set; }
        public string? External { get; set; }
        public int Acknowledgements { get; private set; }
        public bool Acknowledged { get; private set; }
        public Task<string> GetPurchaseTokenAsync(string id, CancellationToken ct) => Task.FromResult(Token);
        public Task<VerifiedGoogleSubscription> VerifyAsync(string token, CancellationToken ct) => Task.FromResult(
            new VerifiedGoogleSubscription("fitmate_ai_plus", "monthly", "SUBSCRIPTION_STATE_ACTIVE", DateTime.UtcNow.AddDays(30), Previous, Acknowledged, Test, Order, External));
        public Task AcknowledgeAsync(string token, CancellationToken ct) { Acknowledgements++; Acknowledged = true; return Task.CompletedTask; }
        public Task<VerifiedPlayOrder> GetOrderAsync(string id, string token, CancellationToken ct) => Task.FromResult(new VerifiedPlayOrder(id, "PROCESSED", DateTime.UtcNow, 30526, 1526, 24650));
    }

    [Theory]
    [InlineData("linked-owner")]
    [InlineData("unknown-owner")]
    [InlineData("test-purchase")]
    [InlineData("conflicting-external")]
    [InlineData("external-owner")]
    public async Task Recovery_uses_verified_ownership_before_acknowledging_and_is_idempotent(string scenario)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var google = new Google { Test = scenario == "test-purchase" };
        var owner = Guid.NewGuid();
        if (scenario == "external-owner")
        {
            db.Users.Add(new Fitmate.Domain.Identity.ApplicationUser { Id = owner, Email = owner+"@example.test", UserName = owner.ToString(), FullName = "Recovery test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            google.External = Hash("fitmate-play:" + owner.ToString("N")).ToLowerInvariant();
        }
        else if (scenario != "unknown-owner")
            db.AiSubscriptions.Add(new AiSubscriptionRecord { Id = Guid.NewGuid(), UserId = owner,
                PurchaseTokenHash = Hash(google.Previous), ProtectedPurchaseToken = "test-only-previous",
                ProductId = "fitmate_ai_plus", BasePlanId = "monthly", State = "SUBSCRIPTION_STATE_EXPIRED",
                CreatedAtUtc = DateTime.UtcNow, VerifiedAtUtc = DateTime.UtcNow, IsTestPurchase = false });
        if (scenario == "conflicting-external") google.External = new string('0', 64);
        await db.SaveChangesAsync();
        var subscriptions = new EfAiSubscriptionService(db, google, Options.Create(new GooglePlayBillingOptions { Enabled = true }), protection);
        var recovery = new GooglePlayOrderRecovery(db, google, google, subscriptions, new AiPlayOrderSync(db, subscriptions, google, protection));
        if (scenario is "linked-owner" or "external-owner")
        {
            await recovery.RecoverAsync(google.Order, default);
            await recovery.RecoverAsync(google.Order, default);
            var order = await db.AiPlayOrders.SingleAsync(x => x.OrderId == google.Order);
            var subscription = await db.AiSubscriptions.SingleAsync(x => x.Id == order.SubscriptionId);
            subscription.UserId.Should().Be(owner);
            order.DeveloperRevenueVnd.Should().Be(24650);
            google.Acknowledgements.Should().Be(1);
            (await db.AiPlayOrders.CountAsync(x => x.OrderId == google.Order)).Should().Be(1);
        }
        else
        {
            var error = await FluentActions.Invoking(() => recovery.RecoverAsync(google.Order, default)).Should().ThrowAsync<DomainException>();
            error.Which.Code.Should().Be(scenario == "test-purchase" ? "AI_ORDER_NOT_REAL" : scenario == "conflicting-external" ? "AI_SUBSCRIPTION_BOUND_TO_ACCOUNT" : "AI_ORDER_OWNER_UNKNOWN");
            google.Acknowledgements.Should().Be(0);
            (await db.AiPlayOrders.AnyAsync(x => x.OrderId == google.Order)).Should().BeFalse();
        }
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
