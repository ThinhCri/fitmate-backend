using Fitmate.Application.Notifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>
/// Background service wrapper for <see cref="PushDeliverySweepService"/>.
/// Runs every 30 minutes. Multiple instances can run concurrently because the
/// sweep uses <c>FOR UPDATE SKIP LOCKED</c> (see
/// <c>IPushStore.ClaimExpiredBatchAsync</c>).
/// </summary>
public class SweepBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SweepBackgroundService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    public SweepBackgroundService(
        IServiceProvider services,
        ILogger<SweepBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Push delivery sweep service starting, interval={Interval}", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                using var scope = _services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<PushDeliverySweepService>();
                var result = await service.SweepOnceAsync(stoppingToken);
                _logger.LogInformation(
                    "Push delivery sweep completed: claimed={Claimed} expired={Expired}",
                    result.Claimed, result.Expired);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Push delivery sweep failed.");
            }
        }

        _logger.LogInformation("Push delivery sweep service stopped.");
    }
}
