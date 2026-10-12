using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public sealed class Ga4Options
{
    public const string SectionName = "Ga4";
    public string PropertyId { get; set; } = string.Empty;
    // Base64 service-account JSON comes from a server secret only; never from mobile or Admin Web.
    public string ServiceAccountJsonBase64 { get; set; } = string.Empty;
}

public sealed record AnalyticsDay(DateOnly Date, long ActiveUsers, long Views);
public sealed record AnalyticsOverview(
    bool Configured, string Range, long? ActiveUsers, long? NewUsers,
    long? Views, long? EventCount, IReadOnlyList<AnalyticsDay> Days);

public interface IGa4ReportProvider
{
    bool IsConfigured { get; }
    Task<AnalyticsOverview> GetAsync(string range, int days, CancellationToken ct);
}

public sealed class AdminAnalyticsService(IGa4ReportProvider provider, IAuthorizationGuard authz)
{
    public async Task<AnalyticsOverview> GetAsync(Guid adminId, string range, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        var days = range switch
        {
            "7d" => 7,
            "30d" => 30,
            "90d" => 90,
            _ => throw new DomainException("RANGE_INVALID", "Use 7d, 30d or 90d.", 400)
        };
        if (!provider.IsConfigured)
            return new AnalyticsOverview(false, range, null, null, null, null, []);
        return await provider.GetAsync(range, days, ct);
    }
}
