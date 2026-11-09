using System.Text.Json.Serialization;

namespace Fitmate.Application.Ai;

public sealed record AiSubscriptionView(
    [property: JsonPropertyName("plan")] string Plan,
    [property: JsonPropertyName("billing_available")] bool BillingAvailable,
    [property: JsonPropertyName("restore_available")] bool RestoreAvailable,
    [property: JsonPropertyName("product_id")] string ProductId,
    [property: JsonPropertyName("base_plan_id")] string BasePlanId,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("expires_at_utc")] DateTime? ExpiresAtUtc,
    [property: JsonPropertyName("billing_account_id")] string? BillingAccountId = null);

public sealed record VerifiedGoogleSubscription(string ProductId, string BasePlanId,
    string State, DateTime? ExpiresAtUtc, string? LinkedPurchaseToken, bool Acknowledged,
    bool? IsTestPurchase = null, string? OrderId = null, string? ExternalAccountId = null);

public interface IGooglePlaySubscriptionVerifier
{
    Task<VerifiedGoogleSubscription> VerifyAsync(string purchaseToken, CancellationToken ct);
    Task AcknowledgeAsync(string purchaseToken, CancellationToken ct);
}

public interface IAiSubscriptionService
{
    Task<AiSubscriptionView> GetAsync(Guid userId, CancellationToken ct);
    Task<AiSubscriptionView> VerifyAsync(Guid userId, string purchaseToken, CancellationToken ct);
}
