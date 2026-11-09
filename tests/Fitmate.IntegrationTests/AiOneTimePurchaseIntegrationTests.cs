using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Http.Json;
using Fitmate.Application.Ai;
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

public sealed class AiOneTimePurchaseIntegrationTests(FitmateTestAppFactory factory) : IClassFixture<FitmateTestAppFactory>
{
    [Fact] public async Task Api_requires_auth_idempotency_and_configured_provider()
    {
        factory.EnsureDatabaseInitialized();
        var anonymous = await factory.CreateClient().PostAsJsonAsync("/api/v1/me/ai-subscription/verify-purchase", new { purchase_token = "unit-token" });
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var client = factory.CreateAuthenticatedClient(role: "Member", userId: Guid.NewGuid());
        var missingKey = await client.PostAsJsonAsync("/api/v1/me/ai-subscription/verify-purchase", new { purchase_token = "unit-token" });
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/ai-subscription/verify-purchase") { Content = JsonContent.Create(new { purchase_token = "unit-token" }) };
        request.Headers.Add("Idempotency-Key", "one-time-config-disabled");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
    private static string Binding(Guid id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fitmate-play:" + id.ToString("N")))).ToLowerInvariant();
    private sealed class Products : IGooglePlayProductVerifier
    {
        public VerifiedGoogleProduct Result = null!;
        public bool FailConsume;
        public int Consumed;
        public Task<VerifiedGoogleProduct> VerifyProductAsync(string token, CancellationToken ct) => Task.FromResult(Result);
        public Task ConsumeAsync(string token, CancellationToken ct)
        {
            if (FailConsume) throw new HttpRequestException("test provider unavailable");
            Consumed++; Result = Result with { Consumed = true }; return Task.CompletedTask;
        }
    }
    private static VerifiedGoogleProduct Paid(Guid user) => new("fitmate_ai_plus_month", "PURCHASED",
        "GPA.1234-5678-9012-34567", Binding(user), DateTime.UtcNow, false, false);
    private sealed class Legacy : IGooglePlaySubscriptionVerifier
    {
        public Task<VerifiedGoogleSubscription> VerifyAsync(string token, CancellationToken ct) => throw new InvalidOperationException("One-time rights must not query the legacy subscription API.");
        public Task AcknowledgeAsync(string token, CancellationToken ct) => throw new InvalidOperationException();
    }

    [Fact] public async Task Two_accounts_keep_independent_paid_rights_and_replay_does_not_extend()
    {
        factory.EnsureDatabaseInitialized();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var google = new Products(); var options = Options.Create(new GooglePlayBillingOptions { Enabled = true });
        var service = new EfAiOneTimePurchaseService(db, google, options, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var tokenA = "one-time-a-" + Guid.NewGuid(); var tokenB = "one-time-b-" + Guid.NewGuid();
        google.Result = Paid(a); await service.VerifyAsync(a, tokenA, default);
        var rowA = await db.AiSubscriptions.SingleAsync(x => x.UserId == a); var expiry = rowA.ExpiresAtUtc;
        await service.CompleteAsync(rowA.Id, default);
        await service.VerifyAsync(a, tokenA, default);
        rowA.ExpiresAtUtc.Should().Be(expiry);
        google.Result = Paid(b) with { OrderId = "GPA.1234-5678-9012-34568" };
        await service.VerifyAsync(b, tokenB, default);
        var rowB = await db.AiSubscriptions.SingleAsync(x => x.UserId == b);
        rowB.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow.AddDays(27));
        await FluentActions.Invoking(() => service.VerifyAsync(a, tokenB, default)).Should().ThrowAsync<DomainException>().Where(x => x.HttpStatus == 409);
        var status = new EfAiSubscriptionService(db, new Legacy(), options, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        (await status.GetAsync(a, default)).Plan.Should().Be("plus");
        (await status.GetAsync(b, default)).Plan.Should().Be("plus");
        (await status.GetAsync(a, default)).ExpiresAtUtc.Should().Be(expiry);
        (await db.OutboxMessages.CountAsync(x => x.Type == "ai.purchase.complete" && (x.PayloadJson.Contains(rowA.Id.ToString()) || x.PayloadJson.Contains(rowB.Id.ToString())))).Should().Be(2);
    }

    [Theory] [InlineData("PENDING", false)] [InlineData("CANCELLED", false)] [InlineData("PURCHASED", true)]
    public async Task Unknown_pending_cancelled_or_consumed_tokens_never_grant(string state, bool consumed)
    {
        factory.EnsureDatabaseInitialized(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>(); var user = Guid.NewGuid();
        var google = new Products { Result = Paid(user) with { State = state, Consumed = consumed } };
        var service = new EfAiOneTimePurchaseService(db, google, Options.Create(new GooglePlayBillingOptions { Enabled = true }), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        await FluentActions.Invoking(() => service.VerifyAsync(user, "unpaid-" + Guid.NewGuid(), default)).Should().ThrowAsync<DomainException>();
        (await db.AiSubscriptions.AnyAsync(x => x.UserId == user)).Should().BeFalse(); google.Consumed.Should().Be(0);
    }

    [Fact] public async Task Consumption_failure_keeps_durable_grant_and_refund_revokes_only_its_owner()
    {
        factory.EnsureDatabaseInitialized(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>(); var user = Guid.NewGuid();
        var google = new Products { Result = Paid(user), FailConsume = true };
        var service = new EfAiOneTimePurchaseService(db, google, Options.Create(new GooglePlayBillingOptions { Enabled = true }), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        await service.VerifyAsync(user, "durable-" + Guid.NewGuid(), default);
        var row = await db.AiSubscriptions.SingleAsync(x => x.UserId == user);
        await FluentActions.Invoking(() => service.CompleteAsync(row.Id, default)).Should().ThrowAsync<HttpRequestException>();
        row.State.Should().Be("SUBSCRIPTION_STATE_ACTIVE");
        google.FailConsume = false; await service.CompleteAsync(row.Id, default);
        google.Result = google.Result with { State = "CANCELLED" };
        await service.ReconcileAsync(row.Id, default);
        row.State.Should().Be("SUBSCRIPTION_STATE_REVOKED");
    }

    [Fact] public async Task Missing_binding_is_rejected_and_second_paid_order_extends_exactly_once()
    {
        factory.EnsureDatabaseInitialized(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>(); var user = Guid.NewGuid();
        var google = new Products { Result = Paid(user) with { ExternalAccountId = null } };
        var service = new EfAiOneTimePurchaseService(db, google, Options.Create(new GooglePlayBillingOptions { Enabled = true }), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var token = "extend-" + Guid.NewGuid();
        await FluentActions.Invoking(() => service.VerifyAsync(user, token, default)).Should().ThrowAsync<DomainException>();
        google.Result = Paid(user); await service.VerifyAsync(user, token, default);
        var firstEnd = (await db.AiSubscriptions.SingleAsync(x => x.UserId == user)).ExpiresAtUtc!.Value;
        google.Result = Paid(user) with { OrderId = "GPA.1234-5678-9012-34569" };
        var second = "extend-second-" + Guid.NewGuid();
        await service.VerifyAsync(user, second, default); await service.VerifyAsync(user, second, default);
        (await db.AiSubscriptions.Where(x => x.UserId == user).MaxAsync(x => x.ExpiresAtUtc)).Should().Be(firstEnd.AddMonths(1));
    }
}
