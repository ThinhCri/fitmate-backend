namespace Fitmate.Domain.Ai;

// Only server-verified non-test purchases may enter this read model.
// Google revenue is not evidence of a payout to the developer's bank.
public sealed class AiPlayOrder
{
    public string OrderId { get; set; } = string.Empty;
    public Guid SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime PurchasedAtUtc { get; set; }
    public long TotalVnd { get; set; }
    public long? TaxVnd { get; set; }
    public long? DeveloperRevenueVnd { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}
