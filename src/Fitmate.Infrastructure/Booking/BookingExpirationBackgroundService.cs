using Fitmate.Application.Booking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fitmate.Infrastructure.Booking;

/// <summary>
/// Background worker định kỳ quét và chuyển trạng thái Expired cho:
/// 1. Held appointments quá hạn hold_expires_at_utc (10 phút)
/// 2. Legacy pending appointments quá hạn acceptance_deadline_utc
/// 3. Completion submissions not disputed within 24 hours
/// </summary>
public class BookingExpirationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BookingExpirationBackgroundService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public BookingExpirationBackgroundService(
        IServiceProvider services,
        ILogger<BookingExpirationBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Booking expiration background service starting, interval={Interval}", Interval);

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
                var packageExpiry = scope.ServiceProvider.GetRequiredService<SessionPackageExpirationService>();
                var expiredPackages = await packageExpiry.ExpireUnpaidAsync(DateTime.UtcNow, stoppingToken);
                var stopService = scope.ServiceProvider.GetRequiredService<SessionPackageStopService>();
                var approvedStops = await stopService.SweepTimedOutAsync(DateTime.UtcNow, stoppingToken);
                var finalizedStops = await stopService.SweepApprovedAsync(DateTime.UtcNow, stoppingToken);
                var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                var expiredCount = await bookingService.ExpireOldHoldsAndPendingAsync(DateTime.UtcNow, stoppingToken);
                if (expiredPackages > 0)
                    _logger.LogInformation("Package expiration sweep completed: {Count} packages expired.", expiredPackages);
                if (approvedStops > 0)
                    _logger.LogInformation("Package stop response sweep approved {Count} timed-out requests.", approvedStops);
                if (finalizedStops > 0)
                    _logger.LogInformation("Package stop finalization sweep completed: {Count} packages stopped.", finalizedStops);
                if (expiredCount > 0)
                {
                    _logger.LogInformation("Booking expiration sweep completed: {Count} appointments expired.", expiredCount);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Booking expiration sweep failed.");
            }
        }

        _logger.LogInformation("Booking expiration background service stopped.");
    }
}
