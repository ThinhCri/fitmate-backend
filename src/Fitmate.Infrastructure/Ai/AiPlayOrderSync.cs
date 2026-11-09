using Fitmate.Application.Ai;
using Fitmate.Domain.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Fitmate.Domain.Outbox;

namespace Fitmate.Infrastructure.Ai;

public sealed class AiPlayOrderSync(FitmateDbContext db, IAiSubscriptionService subscriptions,
    IGooglePlayOrderReader google, IDataProtectionProvider protection, IAiOneTimePurchaseService? oneTime = null)
{
    public async Task SyncAsync(Guid subscriptionId, CancellationToken ct, string? historicalOrderId = null)
    {
        var row = await db.AiSubscriptions.SingleAsync(x => x.Id == subscriptionId, ct);
        var token = protection.CreateProtector("fitmate-google-play-purchase-token-v1").Unprotect(row.ProtectedPurchaseToken);
        // Backfill old records through Google, never assume that a legacy record is a real sale.
        try
        {
            if (row.BasePlanId.Length == 0)
                await (oneTime ?? throw new InvalidOperationException("One-time verifier is required.")).ReconcileAsync(row.Id, ct);
            else await subscriptions.VerifyAsync(row.UserId, token, ct);
        }
        catch (DomainException ex) when (ex.Code == "AI_SUBSCRIPTION_INVALID" &&
            row.IsTestPurchase == false && row.LatestOrderId != null)
        {
            // Old tokens can age out of subscriptionsv2. Keep reconciling an already
            // classified real order via Orders API, including refunds, without granting Plus.
        }
        await db.Entry(row).ReloadAsync(ct);
        if (row.IsTestPurchase != false || string.IsNullOrWhiteSpace(row.LatestOrderId)) return;
        var order = await google.GetOrderAsync(historicalOrderId ?? row.LatestOrderId, token, ct);
        // PostgreSQL timestamp precision is microseconds; normalize before comparison
        // so sub-microsecond provider timestamps do not emit false change events.
        var purchasedAt = new DateTime(order.PurchasedAtUtc.Ticks - order.PurchasedAtUtc.Ticks % 10, DateTimeKind.Utc);
        // OrderId is the idempotency key, including repeated restore/background synchronization.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"ai-order:" + order.OrderId}, 0))", ct);
            var saved = await db.AiPlayOrders.SingleOrDefaultAsync(x => x.OrderId == order.OrderId, ct);
            if (saved != null && saved.SubscriptionId != row.Id)
                throw new DomainException("AI_ORDER_OWNERSHIP_CONFLICT", "Đơn hàng đã thuộc một giao dịch khác.", 409);
            var changed = saved == null || saved.State != order.State || saved.TotalVnd != order.TotalVnd ||
                saved.TaxVnd != order.TaxVnd || saved.DeveloperRevenueVnd != order.DeveloperRevenueVnd ||
                saved.PurchasedAtUtc != purchasedAt;
            saved ??= new AiPlayOrder { OrderId = order.OrderId, SubscriptionId = row.Id };
            if (db.Entry(saved).State == EntityState.Detached) db.AiPlayOrders.Add(saved);
            saved.State = order.State;
            saved.PurchasedAtUtc = purchasedAt;
            saved.TotalVnd = order.TotalVnd;
            saved.TaxVnd = order.TaxVnd;
            saved.DeveloperRevenueVnd = order.DeveloperRevenueVnd;
            saved.SyncedAtUtc = DateTime.UtcNow;
            if (changed) db.OutboxMessages.Add(OutboxMessage.Enqueue("admin.finance.changed", "{}"));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }
}
