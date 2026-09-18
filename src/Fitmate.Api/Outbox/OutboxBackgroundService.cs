using Fitmate.Application.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fitmate.Api.Outbox;

/// <summary>
/// Hosted service chạy <see cref="OutboxWorker"/> mỗi <see cref="Interval"/> ms.
/// Stop gracefully khi host shutdown (qua <see cref="IHostApplicationLifetime"/>).
/// </summary>
public class OutboxBackgroundService : BackgroundService
{
    private readonly OutboxWorker _worker;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public OutboxBackgroundService(
        OutboxWorker worker,
        ILogger<OutboxBackgroundService> logger,
        TimeSpan? interval = null)
    {
        _worker = worker;
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
                var processed = await _worker.ProcessOnceAsync(stoppingToken);
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
