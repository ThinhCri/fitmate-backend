using System.Net;
using System.Net.Http.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Ai;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Fitmate.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.IntegrationTests;

public sealed class AiSalesReportingIntegrationTests(FitmateTestAppFactory factory) : IClassFixture<FitmateTestAppFactory>
{
    private sealed class GoogleStub : IGooglePlaySubscriptionVerifier, IGooglePlayOrderReader
    {
        public bool Test { get; set; }
        public string OrderId { get; set; } = "GPA." + Guid.NewGuid().ToString("N");
        public string OrderState { get; set; } = "PROCESSED";
        public int OrderCalls { get; private set; }
        public DateTime PurchasedAt { get; } = DateTime.UtcNow;
        public Task<VerifiedGoogleSubscription> VerifyAsync(string token, CancellationToken ct) =>
            Task.FromResult(new VerifiedGoogleSubscription("fitmate_ai_plus", "monthly", "SUBSCRIPTION_STATE_ACTIVE", DateTime.UtcNow.AddDays(30), null, true, Test, OrderId));
        public Task AcknowledgeAsync(string token, CancellationToken ct) => Task.CompletedTask;
        public Task<VerifiedPlayOrder> GetOrderAsync(string id, string token, CancellationToken ct)
        {
            OrderCalls++;
            return Task.FromResult(new VerifiedPlayOrder(id, OrderState, PurchasedAt, 29000, 0, OrderState == "REFUNDED" ? 0 : 24650));
        }
    }
    [Fact] public async Task Backfill_excludes_test_purchases_and_repeated_sync_updates_one_order_including_refund()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var google = new GoogleStub { Test = true };
        var sub = new AiSubscriptionRecord { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), PurchaseTokenHash = Guid.NewGuid().ToString("N"),
            ProtectedPurchaseToken = protection.CreateProtector("fitmate-google-play-purchase-token-v1").Protect("test-only-" + Guid.NewGuid()),
            ProductId = "fitmate_ai_plus", BasePlanId = "monthly", State = "SUBSCRIPTION_STATE_ACTIVE", CreatedAtUtc = DateTime.UtcNow, VerifiedAtUtc = DateTime.UtcNow };
        // Hash must match the protected token for the real verification service to find its owner.
        var token = protection.CreateProtector("fitmate-google-play-purchase-token-v1").Unprotect(sub.ProtectedPurchaseToken);
        sub.PurchaseTokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        db.AiSubscriptions.Add(sub); await db.SaveChangesAsync();
        var subscriptions = new EfAiSubscriptionService(db, google, Options.Create(new GooglePlayBillingOptions { Enabled = true }), protection);
        var sync = new AiPlayOrderSync(db, subscriptions, google, protection);
        await sync.SyncAsync(sub.Id, default);
        google.OrderCalls.Should().Be(0);
        (await db.AiPlayOrders.CountAsync(x => x.OrderId == google.OrderId)).Should().Be(0);
        google.Test = false;
        var eventsBefore = await db.OutboxMessages.CountAsync(x => x.Type == "admin.finance.changed");
        await sync.SyncAsync(sub.Id, default); await sync.SyncAsync(sub.Id, default);
        (await db.OutboxMessages.CountAsync(x => x.Type == "admin.finance.changed")).Should().Be(eventsBefore + 1);
        (await db.AiPlayOrders.CountAsync(x => x.OrderId == google.OrderId)).Should().Be(1);
        google.OrderState = "REFUNDED";
        await sync.SyncAsync(sub.Id, default);
        (await db.OutboxMessages.CountAsync(x => x.Type == "admin.finance.changed")).Should().Be(eventsBefore + 2);
        var report = await new EfAdminAiSalesStore(db).ListAsync(new(Range: "all", OrderId: google.OrderId), null, default);
        report.ProcessedGrossVnd.Should().Be(0); report.DeveloperRevenueVnd.Should().Be(0);
        report.Items.Should().ContainSingle(x => x.State == "REFUNDED");
    }
    [Fact] public async Task Api_requires_auth_admin_and_valid_filters()
    {
        (await factory.CreateClient().GetAsync("/api/v1/admin/finance/ai-sales")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateAuthenticatedClient("Member").GetAsync("/api/v1/admin/finance/ai-sales")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var admin = factory.CreateAuthenticatedClient("Admin");
        (await factory.CreateClient().PostAsync("/api/v1/admin/finance/live/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateAuthenticatedClient("Member").PostAsync("/api/v1/admin/finance/live/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync("/api/v1/admin/finance/live/negotiate?negotiateVersion=1", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/v1/admin/finance/ai-sales?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/v1/admin/finance/ai-sales?sort=amount")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await factory.CreateClient().GetAsync("/api/v1/admin/finance/ai-sales/buyers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateAuthenticatedClient("Member").GetAsync("/api/v1/admin/finance/ai-sales/buyers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/admin/finance/ai-sales/buyers?pageSize=101")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact] public async Task Buyers_group_unique_real_orders_with_identity_search_and_user_history()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var id = Guid.NewGuid(); var key = "buyer-" + id.ToString("N"); var now = DateTime.UtcNow;
        db.Users.Add(new Fitmate.Domain.Identity.ApplicationUser { Id = id, FullName = key, Email = key + "@example.test", UserName = key, CreatedAt = now, UpdatedAt = now });
        foreach (var (state, test, amount, net) in new[] { ("PROCESSED", false, 27000L, (long?)22000), ("REFUNDED", false, 29000L, (long?)0), ("PROCESSED", true, 29000L, (long?)24000), ("PENDING", false, 29000L, (long?)null), ("PROCESSED", false, 30526L, (long?)null) })
        {
            var sub = new AiSubscriptionRecord { Id = Guid.NewGuid(), UserId = id, PurchaseTokenHash = Guid.NewGuid().ToString("N"), ProtectedPurchaseToken = "test-only", ProductId = "fitmate_ai_plus", BasePlanId = "monthly", State = "SUBSCRIPTION_STATE_ACTIVE", CreatedAtUtc = now, VerifiedAtUtc = now, IsTestPurchase = test };
            db.AiSubscriptions.Add(sub);
            db.AiPlayOrders.Add(new AiPlayOrder { OrderId = "GPA." + Guid.NewGuid(), SubscriptionId = sub.Id, State = state, PurchasedAtUtc = now, TotalVnd = amount, DeveloperRevenueVnd = net, SyncedAtUtc = now });
        }
        await db.SaveChangesAsync();
        var admin = factory.CreateAuthenticatedClient("Admin");
        var buyers = await admin.GetFromJsonAsync<AiBuyersPage>("/api/v1/admin/finance/ai-sales/buyers?range=all&search=" + key.ToUpperInvariant());
        var buyer = buyers!.Items.Should().ContainSingle().Subject;
        buyer.UserEmail.Should().Be(key + "@example.test"); buyer.PurchaseCount.Should().Be(3);
        buyer.OrderTotalVnd.Should().Be(86526); buyer.RefundedOrders.Should().Be(1); buyer.DeveloperRevenueVnd.Should().BeNull(); buyer.ConfirmedRevenueVnd.Should().Be(22000); buyer.RevenueUnavailableOrders.Should().Be(1);
        var history = await admin.GetFromJsonAsync<AiSalesPage>("/api/v1/admin/finance/ai-sales?range=all&userId=" + id);
        history!.Items.Should().HaveCount(4).And.OnlyContain(x => x.UserId == id && x.UserName == key);
        history.ConfirmedRevenueVnd.Should().Be(22000); history.DeveloperRevenueVnd.Should().BeNull(); history.RevenueUnavailableOrders.Should().Be(1);
        var emailMatch = await admin.GetFromJsonAsync<AiSalesPage>("/api/v1/admin/finance/ai-sales?range=all&search=" + key.Substring(3).ToUpperInvariant());
        emailMatch!.Items.Should().HaveCount(4).And.OnlyContain(x => x.UserId == id);
        var orderMatch = await admin.GetFromJsonAsync<AiSalesPage>("/api/v1/admin/finance/ai-sales?range=all&userId=" + id + "&search=gpa.");
        orderMatch!.Items.Should().HaveCount(4);
        var literal = await admin.GetFromJsonAsync<AiSalesPage>("/api/v1/admin/finance/ai-sales?range=all&userId=" + id + "&search=%25");
        literal!.Total.Should().Be(0);
        var empty = await admin.GetFromJsonAsync<AiBuyersPage>("/api/v1/admin/finance/ai-sales/buyers?search=missing-" + key);
        empty!.Total.Should().Be(0);
    }
    [Fact] public async Task Real_only_filter_unknown_exclusion_and_exact_aggregates_work_in_postgres()
    {
        var prefix = "GPA.integration." + Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var now = DateTime.UtcNow;
        foreach (var (suffix, test, total) in new (string, bool?, long)[] { ("real", false, 27000), ("test", true, 29000), ("unknown", null, 29000) })
        {
            var sub = new AiSubscriptionRecord { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), PurchaseTokenHash = Guid.NewGuid().ToString("N"),
                ProtectedPurchaseToken = "not-a-live-token", ProductId = "fitmate_ai_plus", BasePlanId = "monthly", State = "SUBSCRIPTION_STATE_ACTIVE",
                CreatedAtUtc = now, VerifiedAtUtc = now, IsTestPurchase = test, LatestOrderId = prefix + suffix };
            db.AiSubscriptions.Add(sub);
            db.AiPlayOrders.Add(new AiPlayOrder { OrderId = sub.LatestOrderId, SubscriptionId = sub.Id, State = "PROCESSED",
                PurchasedAtUtc = now, TotalVnd = total, DeveloperRevenueVnd = 22311, SyncedAtUtc = now });
        }
        await db.SaveChangesAsync();
        var admin = factory.CreateAuthenticatedClient("Admin");
        var real = await admin.GetFromJsonAsync<AiSalesPage>("/api/v1/admin/finance/ai-sales?range=all&orderId=" + prefix + "real");
        real!.Items.Should().ContainSingle(); real.ProcessedGrossVnd.Should().Be(27000); real.DeveloperRevenueVnd.Should().Be(22311);
        foreach (var suffix in new[] { "test", "unknown", "notfound" })
        {
            var page = await admin.GetFromJsonAsync<AiSalesPage>("/api/v1/admin/finance/ai-sales?range=all&orderId=" + prefix + suffix);
            page!.Items.Should().BeEmpty(); page.Total.Should().Be(0); page.ProcessedGrossVnd.Should().Be(0);
        }
    }
}
