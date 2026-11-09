using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

public sealed class GooglePlaySubscriptionVerifierTests
{
    private sealed class GoogleHandler(string payload, HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"unit-test-token\"}") });
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.RequestUri.AbsolutePath.Should().Contain("vn.fitmate.app/purchases/subscriptionsv2/tokens/");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(payload) });
        }
    }

    private static GooglePlaySubscriptionVerifier Create(string payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var rsa = RSA.Create(2048);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            client_email = "unit-test@example.invalid", private_key = rsa.ExportPkcs8PrivateKeyPem(), token_uri = "https://oauth2.googleapis.com/token"
        })));
        return new GooglePlaySubscriptionVerifier(new HttpClient(new GoogleHandler(payload, status)),
            Options.Create(new GooglePlayBillingOptions { Enabled = true, ServiceAccountJsonBase64 = credentials }));
    }

    private static string Payload(string product = "fitmate_ai_plus", string plan = "monthly", bool prepaid = true) => JsonSerializer.Serialize(new
    {
        subscriptionState = "SUBSCRIPTION_STATE_ACTIVE", acknowledgementState = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
        lineItems = new[] { new { productId = product, offerDetails = new { basePlanId = plan }, expiryTime = "2099-01-01T00:00:00Z", prepaidPlan = prepaid ? new { } : null, autoRenewingPlan = prepaid ? null : new { } } }
    });

    [Fact] public async Task AcceptsVerifiedPrepaidMonthWithoutTrustingClientPrice()
    {
        var result = await Create(Payload()).VerifyAsync("unit-test-purchase-token", default);
        result.ProductId.Should().Be("fitmate_ai_plus");
        result.BasePlanId.Should().Be("monthly");
        result.Acknowledged.Should().BeTrue();
        result.IsTestPurchase.Should().BeFalse();
    }
    [Fact] public async Task Google_test_purchase_flag_is_preserved_for_reporting_exclusion()
    {
        var payload = Payload().Replace("\"subscriptionState\":", "\"testPurchase\":{},\"subscriptionState\":");
        var result = await Create(payload).VerifyAsync("unit-test-purchase-token", default);
        result.IsTestPurchase.Should().BeTrue();
    }
    [Theory] [InlineData("another_product", "monthly")] [InlineData("fitmate_ai_plus", "annual")]
    public async Task RejectsWrongProductOrBasePlan(string product, string plan)
    {
        var action = () => Create(Payload(product, plan)).VerifyAsync("unit-test-purchase-token", default);
        (await action.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AI_SUBSCRIPTION_WRONG_PRODUCT");
    }
    [Fact] public async Task RejectsAutoRenewingPlanEvenWithMatchingIds()
    {
        var action = () => Create(Payload(prepaid: false)).VerifyAsync("unit-test-purchase-token", default);
        (await action.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AI_SUBSCRIPTION_WRONG_PLAN_TYPE");
    }
    [Fact] public async Task GoogleFailureNeverProducesPaidVerification()
    {
        var action = () => Create("{}", HttpStatusCode.ServiceUnavailable).VerifyAsync("unit-test-purchase-token", default);
        (await action.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("AI_BILLING_UNAVAILABLE");
    }
}
