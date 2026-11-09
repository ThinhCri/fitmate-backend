using System.Text.Json;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Ai;
using FluentAssertions;

namespace Fitmate.UnitTests;

public sealed class GooglePlayProductVerifierTests
{
    private static string Payload(string state = "PURCHASED", int quantity = 1, string product = "fitmate_ai_plus_month",
        string consumption = "CONSUMPTION_STATE_YET_TO_BE_CONSUMED", bool test = false) => JsonSerializer.Serialize(new
        {
            productLineItem = new[] { new { productId = product, productOfferDetails = new { quantity, consumptionState = consumption } } },
            purchaseStateContext = new { purchaseState = state }, orderId = "GPA.1234-5678-9012-34567",
            obfuscatedExternalAccountId = new string('a', 64), purchaseCompletionTime = "2026-11-10T00:00:00Z",
            testPurchaseContext = test ? new { fopType = "TEST" } : null
        });

    [Fact] public void Preserves_real_purchase_binding_and_completion_time()
    {
        using var json = JsonDocument.Parse(Payload());
        var result = GooglePlaySubscriptionVerifier.ParseProduct(json.RootElement, "fitmate_ai_plus_month");
        result.State.Should().Be("PURCHASED"); result.IsTestPurchase.Should().BeFalse();
        result.ExternalAccountId.Should().HaveLength(64); result.Consumed.Should().BeFalse();
        result.PurchasedAtUtc.Should().Be(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
    }
    [Fact] public void Consumed_purchase_remains_a_valid_verified_purchase()
    {
        using var json = JsonDocument.Parse(Payload(consumption: "CONSUMPTION_STATE_CONSUMED", test: true));
        var result = GooglePlaySubscriptionVerifier.ParseProduct(json.RootElement, "fitmate_ai_plus_month");
        result.Consumed.Should().BeTrue(); result.IsTestPurchase.Should().BeTrue();
    }
    [Theory] [InlineData("PENDING")] [InlineData("CANCELLED")]
    public void Pending_and_cancelled_are_not_mislabelled_as_paid(string state)
    {
        using var json = JsonDocument.Parse(Payload(state));
        GooglePlaySubscriptionVerifier.ParseProduct(json.RootElement, "fitmate_ai_plus_month").State.Should().Be(state);
    }
    [Theory] [InlineData(2, "fitmate_ai_plus_month")] [InlineData(1, "another-product")]
    public void Rejects_wrong_product_or_multi_quantity(int quantity, string product)
    {
        using var json = JsonDocument.Parse(Payload(quantity: quantity, product: product));
        Action action = () => GooglePlaySubscriptionVerifier.ParseProduct(json.RootElement, "fitmate_ai_plus_month");
        action.Should().Throw<DomainException>().Which.Code.Should().Be("AI_PURCHASE_INVALID");
    }
    [Fact] public void Accepts_one_time_orders_without_accepting_a_subscription_as_one_time()
    {
        var payload = """{"orderId":"GPA.1234-5678-9012-34567","purchaseToken":"unit-token","state":"PROCESSED","createTime":"2026-11-10T00:00:00Z","lineItems":[{"productId":"fitmate_ai_plus_month","oneTimePurchaseDetails":{}}],"total":{"currencyCode":"VND","units":"29000"}}""";
        using var json = JsonDocument.Parse(payload);
        GooglePlayOrderParser.Parse(json.RootElement, "GPA.1234-5678-9012-34567", "unit-token", "fitmate_ai_plus_month", "").TotalVnd.Should().Be(29000);
        Action wrong = () => GooglePlayOrderParser.Parse(json.RootElement, "GPA.1234-5678-9012-34567", "unit-token", "fitmate_ai_plus_month", "monthly");
        wrong.Should().Throw<DomainException>();
    }
}
