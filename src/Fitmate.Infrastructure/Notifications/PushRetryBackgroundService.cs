using System.Text.Json;
using Fitmate.Application.Notifications.Handlers;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>Retries persisted due deliveries even after the original outbox event completed.</summary>
public sealed class PushRetryBackgroundService(IServiceScopeFactory scopes, IOptions<FcmPushOptions> options,
    ILogger<PushRetryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!options.Value.Enabled) continue;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
                var now = DateTime.UtcNow;
                var due = await db.PushDeliveries.AsNoTracking().Where(d => d.NextAttemptAt <= now &&
                    (d.Status == PushDeliveryStatus.Pending || d.Status == PushDeliveryStatus.Deferred || d.Status == PushDeliveryStatus.FailedRetryable || d.Status == PushDeliveryStatus.Sending))
                    .OrderBy(d => d.NextAttemptAt).Select(d => d.NotificationId).Take(100).Distinct().ToListAsync(stoppingToken);
                var notifications = await db.Notifications.AsNoTracking().Where(n => due.Contains(n.Id)).Take(100).ToListAsync(stoppingToken);
                var handler = scope.ServiceProvider.GetRequiredService<NotificationPushHandler>();
                foreach (var notification in notifications)
                    await handler.HandleAsync(JsonSerializer.Serialize(new {
                        notificationId = notification.Id, recipientId = notification.UserId,
                        sourceType = notification.SourceType, sourceEventId = notification.SourceEventId ?? Guid.Empty
                    }), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Push retry tick failed; persisted deliveries will be retried."); }
        }
    }
}
