using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Fitmate.Infrastructure.Persistence;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly FitmateDbContext _db;
    public DatabaseHealthCheck(FitmateDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("DB OK");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("DB unreachable", ex);
        }
    }
}
