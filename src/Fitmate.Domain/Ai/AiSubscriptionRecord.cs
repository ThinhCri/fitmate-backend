namespace Fitmate.Domain.Ai;

public sealed class AiSubscriptionRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string PurchaseTokenHash { get; set; } = string.Empty;
    public string ProtectedPurchaseToken { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string BasePlanId { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime VerifiedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool? IsTestPurchase { get; set; }
    public string? LatestOrderId { get; set; }
    public DateTime? OrderSyncAtUtc { get; set; }
}
