using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public sealed record BusinessSummary(
    string Range,
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalUsers,
    int NewUsers,
    int Members,
    int VerifiedPts,
    int PendingPts,
    int PaidTransactions,
    int FailedTransactions,
    int PendingTransactions,
    long CollectedVnd,
    long RefundedVnd,
    long RecognizedPlatformFeeVnd,
    long RecognizedTrainerShareVnd,
    long PaidOutVnd,
    long PendingPayoutVnd,
    int ReviewsCount,
    decimal AverageRating);

public interface IAdminBusinessSummaryStore
{
    Task<BusinessSummary> GetAsync(string range, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
}

public sealed class AdminBusinessSummaryService(
    IAdminBusinessSummaryStore store, IAuthorizationGuard authz)
{
    public async Task<BusinessSummary> GetAsync(Guid adminId, string range, CancellationToken ct)
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
