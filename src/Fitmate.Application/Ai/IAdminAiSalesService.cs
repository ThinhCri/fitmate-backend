using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Ai;

public sealed record VerifiedPlayOrder(string OrderId, string State, DateTime PurchasedAtUtc,
    long TotalVnd, long? TaxVnd, long? DeveloperRevenueVnd);
public interface IGooglePlayOrderReader
{
    Task<VerifiedPlayOrder> GetOrderAsync(string orderId, string purchaseToken, CancellationToken ct);
}
public sealed record AiSalesQuery(string Range = "30d", string? State = null, string? OrderId = null,
    string Sort = "newest", int Page = 1, int PageSize = 25, string? Search = null, Guid? UserId = null);
public sealed record AiSaleRow(string OrderId, Guid UserId, string ProductId, string BasePlanId,
    string State, DateTime PurchasedAtUtc, long TotalVnd, long? TaxVnd, long? DeveloperRevenueVnd,
    DateTime SyncedAtUtc, string? UserName = null, string? UserEmail = null);
public sealed record AiBuyerRow(Guid UserId, string? UserName, string? UserEmail, string ProductId,
    string BasePlanId, int PurchaseCount, long OrderTotalVnd, int RefundedOrders, long? DeveloperRevenueVnd,
    DateTime LastPurchasedAtUtc, long ConfirmedRevenueVnd = 0, int RevenueUnavailableOrders = 0);
public sealed record AiBuyersPage(IReadOnlyList<AiBuyerRow> Items, int Total, int Page, int PageSize);
public sealed record AiSalesPage(IReadOnlyList<AiSaleRow> Items, int Total, int Page, int PageSize,
    int ProcessedOrders, long ProcessedGrossVnd, long? DeveloperRevenueVnd,
    int RevenueUnavailableOrders, int AwaitingSync, DateTime? LastSyncedAtUtc, long ConfirmedRevenueVnd = 0);
public interface IAdminAiSalesStore
{
    Task<AiSalesPage> ListAsync(AiSalesQuery query, DateTime? fromUtc, CancellationToken ct);
    Task<AiBuyersPage> BuyersAsync(AiSalesQuery query, DateTime? fromUtc, CancellationToken ct);
}
public interface IAdminAiSalesService
{
    Task<AiSalesPage> ListAsync(Guid adminId, AiSalesQuery query, CancellationToken ct);
    Task<AiBuyersPage> BuyersAsync(Guid adminId, AiSalesQuery query, CancellationToken ct);
}
public sealed class AdminAiSalesService(IAuthorizationGuard authorization, IAdminAiSalesStore store)
    : IAdminAiSalesService
{
    public async Task<AiSalesPage> ListAsync(Guid adminId, AiSalesQuery query, CancellationToken ct)
    {
        var from = await ValidateAsync(adminId, query, ct);
        return await store.ListAsync(query, from, ct);
    }
    public async Task<AiBuyersPage> BuyersAsync(Guid adminId, AiSalesQuery query, CancellationToken ct)
    {
        var from = await ValidateAsync(adminId, query, ct);
        return await store.BuyersAsync(query, from, ct);
    }
    private async Task<DateTime?> ValidateAsync(Guid adminId, AiSalesQuery query, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(adminId, "Admin", ct);
        if (query.Page < 1 || query.Page > 100000 || query.PageSize < 1 || query.PageSize > 100 ||
            query.Sort is not ("newest" or "oldest") || query.OrderId?.Length > 128 || query.Search?.Length > 200 ||
            query.State is not (null or "" or "PROCESSED" or "PENDING" or "CANCELED" or "PENDING_REFUND" or "PARTIALLY_REFUNDED" or "REFUNDED"))
            throw new DomainException("VALIDATION_ERROR", "Bộ lọc hoặc phân trang không hợp lệ.", 400);
        // Vietnam calendar days, consistent with the AI quota business timezone.
        var todayUtc = DateTime.UtcNow.AddHours(7).Date.AddHours(-7);
        DateTime? from = query.Range switch
        {
            "7d" => todayUtc.AddDays(-6), "30d" => todayUtc.AddDays(-29),
            "90d" => todayUtc.AddDays(-89), "all" => null,
            _ => throw new DomainException("VALIDATION_ERROR", "Khoảng thời gian không hợp lệ.", 400)
        };
        return from;
    }
}
