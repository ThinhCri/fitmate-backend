using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Fitmate.IntegrationTests;

public sealed class GooglePlayPrepaidVerifierTests
{
    [Theory]
    [InlineData("prepaidPlan", true)]
    [InlineData("autoRenewingPlan", false)]
    public async Task Only_prepaid_plan_is_accepted(string planType, bool accepted)
    {
        using var rsa = RSA.Create(2048);
        var credentials = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["client_email"] = "test@example.invalid",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        });
        using var client = new HttpClient(new PlayHandler(planType));
        var verifier = new GooglePlaySubscriptionVerifier(client, Options.Create(new GooglePlayBillingOptions
        {
            Enabled = true,
            ServiceAccountJsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials))
        }));

        if (accepted)
        {
            var result = await verifier.VerifyAsync("purchase-token", CancellationToken.None);
            result.State.Should().Be("SUBSCRIPTION_STATE_ACTIVE");
            result.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        }
        else
        {
            await FluentActions.Invoking(() => verifier.VerifyAsync("purchase-token", CancellationToken.None))
                .Should().ThrowAsync<DomainException>()
                .Where(x => x.Code == "AI_SUBSCRIPTION_WRONG_PLAN_TYPE" && x.HttpStatus == 400);
        }
    }

    private sealed class PlayHandler(string planType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body = request.RequestUri!.Host == "oauth2.googleapis.com"
                ? new { access_token = "test-access-token" }
                : new
                {
                    subscriptionState = "SUBSCRIPTION_STATE_ACTIVE",
                    acknowledgementState = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
                    lineItems = new[]
                    {
                        new Dictionary<string, object>
                        {
                            ["productId"] = "fitmate_ai_plus",
                            ["expiryTime"] = DateTime.UtcNow.AddDays(20).ToString("O"),
                            ["offerDetails"] = new { basePlanId = "monthly" },
                            [planType] = planType == "prepaidPlan"
                                ? new Dictionary<string, object>()
                                : new Dictionary<string, object> { ["autoRenewEnabled"] = true }
                        }
                    }
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            });
        }
    }
}
