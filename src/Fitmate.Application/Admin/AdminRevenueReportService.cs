using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public sealed record RevenueDay(
    DateOnly Date, long CollectedVnd, long RefundedVnd, long PlatformFeeVnd,
    long TrainerShareVnd, long PaidOutVnd);

public sealed record RevenueReport(
    string Range, DateTime FromUtc, DateTime ToUtc,
    long CollectedVnd, long RefundedVnd, long PlatformFeeVnd,
    long TrainerShareVnd, long PaidOutVnd, IReadOnlyList<RevenueDay> Days);

public interface IAdminRevenueReportStore
{
    Task<RevenueReport> GetAsync(string range, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed class AdminRevenueReportService(IAdminRevenueReportStore store, IAuthorizationGuard authz)
{
    public async Task<RevenueReport> GetAsync(Guid adminId, string range, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        var now = DateTime.UtcNow;
        var from = range switch
        {
            "7d" => now.AddDays(-7),
            "30d" => now.AddDays(-30),
            "90d" => now.AddDays(-90),
            "all" => DateTime.UnixEpoch,
            _ => throw new DomainException("RANGE_INVALID", "Use 7d, 30d, 90d or all.", 400)
        };
        return await store.GetAsync(range, from, now, ct);
    }
}
