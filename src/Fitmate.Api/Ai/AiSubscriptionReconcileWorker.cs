using Fitmate.Application.Ai;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fitmate.Api.Ai;

/// <summary>
/// Small-scale reconciliation for renewals, holds, refunds and missed Play events.
/// Never grants entitlement from a notification or client claim: GetAsync re-queries Google.
/// </summary>
public sealed class AiSubscriptionReconcileWorker(IServiceScopeFactory scopes,
    IOptions<GooglePlayBillingOptions> options, ILogger<AiSubscriptionReconcileWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
                var users = await db.AiSubscriptions.AsNoTracking()
                    .Select(x => x.UserId).Distinct().ToListAsync(stoppingToken);
                foreach (var userId in users)
                {
                    try
                    {
                        using var userScope = scopes.CreateScope();
                        await userScope.ServiceProvider.GetRequiredService<IAiSubscriptionService>()
                            .GetAsync(userId, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        // No purchase token, credentials, or raw Play response in logs.
                        logger.LogWarning("AI subscription reconciliation failed: {ExceptionType}", ex.GetType().Name);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                logger.LogWarning("AI subscription reconciliation pass failed: {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
