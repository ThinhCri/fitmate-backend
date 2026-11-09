using Fitmate.Application.Ai;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Ai;

public sealed class EfAdminAiSalesStore(FitmateDbContext db) : IAdminAiSalesStore
{
    private sealed class Sale {
        public long TotalVnd { get; init; }
        public DateTime SyncedAtUtc { get; init; }
        public DateTime PurchasedAtUtc { get; init; }
        public Guid UserId { get; init; }
        public string OrderId { get; init; } = string.Empty;
        public string? UserEmail { get; init; }
        public string? UserName { get; init; }
        public string ProductId { get; init; } = string.Empty;
        public long? DeveloperRevenueVnd { get; init; }
        public string BasePlanId { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public long? TaxVnd { get; init; }
    }

    private IQueryable<Sale> Filter(AiSalesQuery query, DateTime? fromUtc)
    {
        var rows = from order in db.AiPlayOrders.AsNoTracking()
                   join sub in db.AiSubscriptions.AsNoTracking() on order.SubscriptionId equals sub.Id
                   join user in db.Users.AsNoTracking() on sub.UserId equals user.Id into users
                   from user in users.DefaultIfEmpty()
                   where sub.IsTestPurchase == false
                   select new Sale {
                       OrderId = order.OrderId, UserId = sub.UserId, ProductId = sub.ProductId, BasePlanId = sub.BasePlanId,
                       State = order.State, PurchasedAtUtc = order.PurchasedAtUtc, TotalVnd = order.TotalVnd, TaxVnd = order.TaxVnd,
                       DeveloperRevenueVnd = order.DeveloperRevenueVnd, SyncedAtUtc = order.SyncedAtUtc,
                       UserName = user == null ? null : user.FullName, UserEmail = user == null ? null : user.Email };
        if (fromUtc.HasValue) rows = rows.Where(x => x.PurchasedAtUtc >= fromUtc.Value);
        if (!string.IsNullOrEmpty(query.State)) rows = rows.Where(x => x.State == query.State);
        if (!string.IsNullOrWhiteSpace(query.OrderId)) rows = rows.Where(x => x.OrderId == query.OrderId.Trim());
        if (query.UserId.HasValue) rows = rows.Where(x => x.UserId == query.UserId.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Literal substring, not a client-supplied SQL wildcard pattern.
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(x => x.OrderId.ToLower().Contains(term) || (x.UserName != null && x.UserName.ToLower().Contains(term)) ||
                (x.UserEmail != null && x.UserEmail.ToLower().Contains(term)));
        }
        return rows;
    }
    public async Task<AiSalesPage> ListAsync(AiSalesQuery query, DateTime? fromUtc, CancellationToken ct)
    {
        var rows = Filter(query, fromUtc);
        var total = await rows.CountAsync(ct);
        var processed = rows.Where(x => x.State == "PROCESSED");
        var processedCount = await processed.CountAsync(ct);
        var gross = await processed.SumAsync(x => (long?)x.TotalVnd, ct) ?? 0;
        var revenueRows = rows.Where(x => x.State == "PROCESSED" || x.State == "PARTIALLY_REFUNDED" || x.State == "REFUNDED");
        var missing = await revenueRows.CountAsync(x => x.DeveloperRevenueVnd == null, ct);
        var revenue = await revenueRows.SumAsync(x => x.DeveloperRevenueVnd, ct);
        var syncedAt = await rows.MaxAsync(x => (DateTime?)x.SyncedAtUtc, ct);
        rows = query.Sort == "oldest" ? rows.OrderBy(x => x.PurchasedAtUtc).ThenBy(x => x.OrderId)
            : rows.OrderByDescending(x => x.PurchasedAtUtc).ThenBy(x => x.OrderId);
        var items = await rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var awaiting = await db.AiSubscriptions.CountAsync(x => x.IsTestPurchase == false &&
            x.LatestOrderId != null && !db.AiPlayOrders.Any(o => o.OrderId == x.LatestOrderId), ct);
        return new(items.Select(x => new AiSaleRow(x.OrderId, x.UserId, x.ProductId, x.BasePlanId, x.State, x.PurchasedAtUtc, x.TotalVnd, x.TaxVnd, x.DeveloperRevenueVnd, x.SyncedAtUtc, x.UserName, x.UserEmail)).ToList(), total, query.Page, query.PageSize, processedCount, gross,
            missing == 0 ? revenue : null, missing, awaiting, syncedAt, revenue ?? 0);
    }
    private sealed class BuyerTotals
    {
        public Guid UserId { get; init; }
        public string? UserName { get; init; }
        public string? UserEmail { get; init; }
        public string ProductId { get; init; } = string.Empty;
        public string BasePlanId { get; init; } = string.Empty;
        public int PurchaseCount { get; init; }
        public long OrderTotalVnd { get; init; }
        public int RefundedOrders { get; init; }
        public int MissingRevenue { get; init; }
        public long? Revenue { get; init; }
        public DateTime LastPurchasedAtUtc { get; init; }
    }
    private IQueryable<BuyerTotals> BuyerGroups(AiSalesQuery query, DateTime? fromUtc)
    {
        // One stored row per unique Google order: restore/reverification never adds a purchase.
        return Filter(query, fromUtc)
            .Where(x => x.State == "PROCESSED" || x.State == "PARTIALLY_REFUNDED" || x.State == "REFUNDED" || x.State == "PENDING_REFUND")
            .GroupBy(x => new { x.UserId, x.UserName, x.UserEmail, x.ProductId, x.BasePlanId })
            .Select(g => new BuyerTotals { UserId = g.Key.UserId, UserName = g.Key.UserName, UserEmail = g.Key.UserEmail, ProductId = g.Key.ProductId, BasePlanId = g.Key.BasePlanId,
                PurchaseCount = g.Count(), OrderTotalVnd = g.Sum(x => x.TotalVnd),
                RefundedOrders = g.Count(x => x.State == "PARTIALLY_REFUNDED" || x.State == "REFUNDED"),
                MissingRevenue = g.Count(x => x.DeveloperRevenueVnd == null),
                Revenue = g.Sum(x => x.DeveloperRevenueVnd), LastPurchasedAtUtc = g.Max(x => x.PurchasedAtUtc) });
    }
    public async Task<AiBuyersPage> BuyersAsync(AiSalesQuery query, DateTime? fromUtc, CancellationToken ct)
    {
        var groups = BuyerGroups(query, fromUtc);
        var total = await groups.CountAsync(ct);
        groups = query.Sort == "oldest" ? groups.OrderBy(x => x.LastPurchasedAtUtc).ThenBy(x => x.UserId).ThenBy(x => x.ProductId).ThenBy(x => x.BasePlanId)
            : groups.OrderByDescending(x => x.LastPurchasedAtUtc).ThenBy(x => x.UserId).ThenBy(x => x.ProductId).ThenBy(x => x.BasePlanId);
        var page = await groups.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(page.Select(x => new AiBuyerRow(x.UserId, x.UserName, x.UserEmail, x.ProductId, x.BasePlanId,
            x.PurchaseCount, x.OrderTotalVnd, x.RefundedOrders, x.MissingRevenue == 0 ? x.Revenue : null, x.LastPurchasedAtUtc,
            x.Revenue ?? 0, x.MissingRevenue)).ToList(), total, query.Page, query.PageSize);
    }
}


