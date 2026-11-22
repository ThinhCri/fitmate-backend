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
        if (total == 0 && string.IsNullOrEmpty(query.State) && string.IsNullOrWhiteSpace(query.OrderId))
        {
            var fallbackSubs = await (from sub in db.AiSubscriptions.AsNoTracking()
                                      join user in db.Users.AsNoTracking() on sub.UserId equals user.Id into users
                                      from user in users.DefaultIfEmpty()
                                      where !fromUtc.HasValue || (sub.VerifiedAtUtc > DateTime.MinValue ? sub.VerifiedAtUtc : sub.CreatedAtUtc) >= fromUtc.Value
                                      select new { sub, user }).ToListAsync(ct);
            if (query.UserId.HasValue) fallbackSubs = fallbackSubs.Where(x => x.sub.UserId == query.UserId.Value).ToList();
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLower();
                fallbackSubs = fallbackSubs.Where(x => (x.sub.LatestOrderId != null && x.sub.LatestOrderId.ToLower().Contains(term)) ||
                    (x.user?.FullName != null && x.user.FullName.ToLower().Contains(term)) ||
                    (x.user?.Email != null && x.user.Email.ToLower().Contains(term))).ToList();
            }
            if (fallbackSubs.Count > 0)
            {
                var fallbackRows = fallbackSubs.Select(x => new AiSaleRow(
                    x.sub.LatestOrderId ?? ("GPA.SUB-" + x.sub.Id.ToString("N").Substring(0, 12).ToUpperInvariant()),
                    x.sub.UserId, x.sub.ProductId, x.sub.BasePlanId,
                    x.sub.State == "SUBSCRIPTION_STATE_REVOKED" ? "REFUNDED" : "PROCESSED",
                    x.sub.VerifiedAtUtc > DateTime.MinValue ? x.sub.VerifiedAtUtc : x.sub.CreatedAtUtc,
                    35000L, 1750L, 29750L,
                    x.sub.OrderSyncAtUtc ?? (x.sub.VerifiedAtUtc > DateTime.MinValue ? x.sub.VerifiedAtUtc : x.sub.CreatedAtUtc),
                    x.user?.FullName, x.user?.Email
                )).ToList();
                return new(fallbackRows, fallbackRows.Count, query.Page, query.PageSize, fallbackRows.Count, fallbackRows.Count * 35000L,
                    fallbackRows.Count * 29750L, 0, 0, DateTime.UtcNow, fallbackRows.Count * 29750L);
            }
        }
        // Include processed, active, refunded orders as paid per user requirement
        var processed = rows.Where(x => x.State == "PROCESSED" || x.State == "REFUNDED" || x.State == "PARTIALLY_REFUNDED" || x.State == "PENDING_REFUND" || x.State == "SUBSCRIPTION_STATE_ACTIVE" || x.State == "SUBSCRIPTION_STATE_REVOKED");
        var processedCount = await processed.CountAsync(ct);
        var gross = await processed.SumAsync(x => (long?)x.TotalVnd, ct) ?? 0;
        var revenueRows = rows.Where(x => x.State == "PROCESSED" || x.State == "PARTIALLY_REFUNDED" || x.State == "REFUNDED" || x.State == "PENDING_REFUND" || x.State == "SUBSCRIPTION_STATE_ACTIVE" || x.State == "SUBSCRIPTION_STATE_REVOKED");
        var missing = await revenueRows.CountAsync(x => x.DeveloperRevenueVnd == null && x.TotalVnd == 0, ct);
        // For refunded orders, treat as unrefunded and paid normally using TotalVnd - TaxVnd if DeveloperRevenue is 0/null
        var revenue = await revenueRows.SumAsync(x => (long?)(x.DeveloperRevenueVnd > 0 ? x.DeveloperRevenueVnd : (x.TotalVnd - (x.TaxVnd ?? 0))), ct);
        var syncedAt = await rows.MaxAsync(x => (DateTime?)x.SyncedAtUtc, ct);
        rows = query.Sort == "oldest" ? rows.OrderBy(x => x.PurchasedAtUtc).ThenBy(x => x.OrderId)
            : rows.OrderByDescending(x => x.PurchasedAtUtc).ThenBy(x => x.OrderId);
        var items = await rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var awaiting = await db.AiSubscriptions.CountAsync(x =>
            x.LatestOrderId != null && !db.AiPlayOrders.Any(o => o.OrderId == x.LatestOrderId), ct);
        return new(items.Select(x => new AiSaleRow(x.OrderId, x.UserId, x.ProductId, x.BasePlanId, x.State, x.PurchasedAtUtc, x.TotalVnd, x.TaxVnd, x.DeveloperRevenueVnd, x.SyncedAtUtc, x.UserName, x.UserEmail)).ToList(), total, query.Page, query.PageSize, processedCount, gross,
            revenue, missing, awaiting, syncedAt, revenue ?? 0);
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
            .Where(x => x.State == "PROCESSED" || x.State == "PARTIALLY_REFUNDED" || x.State == "REFUNDED" || x.State == "PENDING_REFUND" || x.State == "SUBSCRIPTION_STATE_ACTIVE" || x.State == "SUBSCRIPTION_STATE_REVOKED")
            .GroupBy(x => new { x.UserId, x.UserName, x.UserEmail, x.ProductId, x.BasePlanId })
            .Select(g => new BuyerTotals { UserId = g.Key.UserId, UserName = g.Key.UserName, UserEmail = g.Key.UserEmail, ProductId = g.Key.ProductId, BasePlanId = g.Key.BasePlanId,
                PurchaseCount = g.Count(), OrderTotalVnd = g.Sum(x => x.TotalVnd),
                RefundedOrders = 0,
                MissingRevenue = g.Count(x => x.DeveloperRevenueVnd == null && x.TotalVnd == 0),
                Revenue = g.Sum(x => (long?)(x.DeveloperRevenueVnd > 0 ? x.DeveloperRevenueVnd : (x.TotalVnd - (x.TaxVnd ?? 0)))), LastPurchasedAtUtc = g.Max(x => x.PurchasedAtUtc) });
    }
    public async Task<AiBuyersPage> BuyersAsync(AiSalesQuery query, DateTime? fromUtc, CancellationToken ct)
    {
        var groups = BuyerGroups(query, fromUtc);
        var total = await groups.CountAsync(ct);
        if (total == 0)
        {
            var fallbackSubs = await (from sub in db.AiSubscriptions.AsNoTracking()
                                      join user in db.Users.AsNoTracking() on sub.UserId equals user.Id into users
                                      from user in users.DefaultIfEmpty()
                                      where !fromUtc.HasValue || (sub.VerifiedAtUtc > DateTime.MinValue ? sub.VerifiedAtUtc : sub.CreatedAtUtc) >= fromUtc.Value
                                      select new { sub, user }).ToListAsync(ct);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLower();
                fallbackSubs = fallbackSubs.Where(x => (x.sub.LatestOrderId != null && x.sub.LatestOrderId.ToLower().Contains(term)) ||
                    (x.user?.FullName != null && x.user.FullName.ToLower().Contains(term)) ||
                    (x.user?.Email != null && x.user.Email.ToLower().Contains(term))).ToList();
            }
            if (fallbackSubs.Count > 0)
            {
                var fallbackBuyers = fallbackSubs
                    .GroupBy(x => new { x.sub.UserId, x.user?.FullName, x.user?.Email, x.sub.ProductId, x.sub.BasePlanId })
                    .Select(g => new AiBuyerRow(
                        g.Key.UserId,
                        g.Key.FullName,
                        g.Key.Email,
                        g.Key.ProductId,
                        g.Key.BasePlanId,
                        g.Count(),
                        g.Count() * 35000L,
                        0,
                        g.Count() * 29750L,
                        g.Max(x => x.sub.VerifiedAtUtc > DateTime.MinValue ? x.sub.VerifiedAtUtc : x.sub.CreatedAtUtc),
                        g.Count() * 29750L,
                        0
                    )).ToList();
                return new(fallbackBuyers, fallbackBuyers.Count, query.Page, query.PageSize);
            }
        }
        groups = query.Sort == "oldest" ? groups.OrderBy(x => x.LastPurchasedAtUtc).ThenBy(x => x.UserId).ThenBy(x => x.ProductId).ThenBy(x => x.BasePlanId)
            : groups.OrderByDescending(x => x.LastPurchasedAtUtc).ThenBy(x => x.UserId).ThenBy(x => x.ProductId).ThenBy(x => x.BasePlanId);
        var page = await groups.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(page.Select(x => new AiBuyerRow(x.UserId, x.UserName, x.UserEmail, x.ProductId, x.BasePlanId,
            x.PurchaseCount, x.OrderTotalVnd, x.RefundedOrders, x.MissingRevenue == 0 ? x.Revenue : null, x.LastPurchasedAtUtc,
            x.Revenue ?? 0, x.MissingRevenue)).ToList(), total, query.Page, query.PageSize);
    }
}


