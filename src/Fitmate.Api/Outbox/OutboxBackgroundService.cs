using Fitmate.Application.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fitmate.Api.Outbox;

/// <summary>
/// Hosted service chạy <see cref="OutboxWorker"/> mỗi <see cref="Interval"/> ms.
/// Uses <see cref="IServiceScopeFactory"/> to resolve scoped <see cref="OutboxWorker"/> safely.
/// Stop gracefully khi host shutdown (qua <see cref="IHostApplicationLifetime"/>).
/// </summary>
public class OutboxBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public OutboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxBackgroundService> logger,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox worker started, interval={Interval}", _interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Create a new scope per tick so scoped services (OutboxWorker, DbContext) are released.
                using var scope = _scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<OutboxWorker>();
                var processed = await worker.ProcessOnceAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox worker tick failed");
                await Task.Delay(_interval, stoppingToken);
            }
        }
        _logger.LogInformation("Outbox worker stopped");
    }
}
