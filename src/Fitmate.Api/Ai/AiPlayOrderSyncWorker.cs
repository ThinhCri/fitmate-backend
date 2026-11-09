using Fitmate.Application.Ai;
using Fitmate.Infrastructure.Ai;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Api.Ai;

public sealed class AiPlayOrderSyncWorker(IServiceScopeFactory scopes, IOptions<GooglePlayBillingOptions> options,
    ILogger<AiPlayOrderSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
                var ids = await DueIds(db, DateTime.UtcNow).ToListAsync(ct);
                foreach (var id in ids)
                {
                    using var rowScope = scopes.CreateScope();
                    try { await rowScope.ServiceProvider.GetRequiredService<AiPlayOrderSync>().SyncAsync(id, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogWarning("AI order synchronization failed: {ExceptionType}", ex.GetType().Name); }
                    // Attempt time prevents a failing provider from creating an unbounded retry loop.
                    await rowScope.ServiceProvider.GetRequiredService<FitmateDbContext>().AiSubscriptions
                        .Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderSyncAtUtc, DateTime.UtcNow), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("AI order synchronization pass failed: {ExceptionType}", ex.GetType().Name); }
        }
    }

    public static IQueryable<Guid> DueIds(FitmateDbContext db, DateTime now)
    {
        var retry = now.AddMinutes(-1);
        var routine = now.AddMinutes(-15);
        return (from sub in db.AiSubscriptions.AsNoTracking()
                let urgent = sub.IsTestPurchase == false && sub.LatestOrderId != null &&
                    (!db.AiPlayOrders.Any(o => o.OrderId == sub.LatestOrderId) ||
                     db.AiPlayOrders.Any(o => o.OrderId == sub.LatestOrderId && o.DeveloperRevenueVnd == null))
                where sub.OrderSyncAtUtc == null || sub.OrderSyncAtUtc < routine || urgent && sub.OrderSyncAtUtc < retry
                orderby sub.OrderSyncAtUtc != null, urgent descending, sub.OrderSyncAtUtc, sub.Id
                select sub.Id).Take(10);
    }
}
