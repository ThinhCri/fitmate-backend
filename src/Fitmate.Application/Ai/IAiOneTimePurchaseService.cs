namespace Fitmate.Application.Ai;

public sealed record VerifiedGoogleProduct(string ProductId, string State, string OrderId,
    string? ExternalAccountId, DateTime? PurchasedAtUtc, bool Consumed, bool IsTestPurchase);

public interface IGooglePlayProductVerifier
{
    Task<VerifiedGoogleProduct> VerifyProductAsync(string token, CancellationToken ct);
    Task ConsumeAsync(string token, CancellationToken ct);
}

public interface IAiOneTimePurchaseService
{
    Task VerifyAsync(Guid userId, string token, CancellationToken ct);
    Task ReconcileAsync(Guid recordId, CancellationToken ct);
    Task CompleteAsync(Guid recordId, CancellationToken ct);
}
