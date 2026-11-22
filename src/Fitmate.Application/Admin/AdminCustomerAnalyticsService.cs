using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public sealed record CustomerAnalyticsQuery(
    string? Range = "30d",
    string? Search = null,
    string? Segment = "all", // "all", "ai_only", "pt_only", "dual"
    string? Sort = "spent_desc", // "spent_desc", "spent_asc", "pt_desc", "ai_desc", "recent", "oldest"
    int Page = 1,
    int PageSize = 25
);

public sealed record CustomerAnalyticsSummary(
    int TotalRegisteredUsers,
    int TotalPayingUsers,
    decimal ConversionRatePercent,
    int AiBuyersCount,
    int PtClientsCount,
    int DualCustomersCount,
    int AiOnlyCount,
    int PtOnlyCount,
    int FreeUsersCount,
    long TotalAiRevenueVnd,
    long TotalPtRevenueVnd,
    long TotalCombinedRevenueVnd,
    long AverageRevenuePerPayingUserVnd,
    int TotalAiOrders,
    int TotalPtBookings
);

public sealed record CustomerPayingDetail(
    Guid UserId,
    string FullName,
    string Email,
    string? PhoneNumber,
    DateTime RegisteredAtUtc,
    int PtBookingCount,
    int AiPurchaseCount,
    long TotalPtSpentVnd,
    long TotalAiSpentVnd,
    long TotalSpentVnd,
    string Segment, // "AI_ONLY" | "PT_ONLY" | "DUAL"
    DateTime? LastTransactionAtUtc
);

public sealed record CustomerAnalyticsPage(
    CustomerAnalyticsSummary Summary,
    IReadOnlyList<CustomerPayingDetail> Items,
    int Total,
    int Page,
    int PageSize
);

public interface IAdminCustomerAnalyticsStore
{
    Task<CustomerAnalyticsPage> GetAsync(CustomerAnalyticsQuery query, CancellationToken ct);
}

public sealed class AdminCustomerAnalyticsService(IAdminCustomerAnalyticsStore store, IAuthorizationGuard authz)
{
    public async Task<CustomerAnalyticsPage> GetAsync(Guid adminId, CustomerAnalyticsQuery query, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var sanitizedQuery = query with { Page = page, PageSize = pageSize };
        return await store.GetAsync(sanitizedQuery, ct);
    }
}
