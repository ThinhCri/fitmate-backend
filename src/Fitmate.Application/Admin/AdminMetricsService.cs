using Fitmate.Application.Auth;

namespace Fitmate.Application.Admin;

/// <summary>
/// BE-220: Admin metrics + action-required (OpenAPI <c>/admin/action-required</c>).
/// Tổng hợp hàng đợi admin cần xử lý:
///   - PendingCertificates: Certificate.Status = Pending.
///   - PendingReports: Report.Status = Pending.
///   - PendingPayments: Payment.Status = Pending (chưa thanh toán xong, cần đối soát).
///   - RecentBookings: N appointment gần nhất (Paid/Completed/Refunded/Cancelled), mặc định 10.
/// Authorization 2 lớp (Authorize + Application guard).
/// </summary>
public class AdminMetricsService : IAdminMetricsService
{
    private readonly IAdminMetricsStore _store;
    private readonly IAuthorizationGuard _authz;

    private const int DefaultRecentLimit = 10;
    private const int MaxRecentLimit = 50;

    public AdminMetricsService(IAdminMetricsStore store, IAuthorizationGuard authz)
    {
        _store = store;
        _authz = authz;
    }

    public async Task<ActionRequired> GetActionRequiredAsync(Guid adminUserId, int recentLimit, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (recentLimit < 1) recentLimit = DefaultRecentLimit;
        if (recentLimit > MaxRecentLimit) recentLimit = MaxRecentLimit;

        var pendingCerts = await _store.CountPendingCertificatesAsync(ct);
        var pendingReports = await _store.CountPendingReportsAsync(ct);
        var pendingPayments = await _store.CountPendingPaymentsAsync(ct);
        var recent = await _store.ListRecentAppointmentsAsync(recentLimit, ct);

        var summaries = recent.Select(r => new RecentBookingSummary(
            AppointmentId: r.AppointmentId,
            MemberId: r.MemberId,
            PtUserId: r.PtUserId,
            ActualPaidVnd: r.ActualPaidVnd,
            Status: r.Status.ToString(),
            CreatedAt: r.CreatedAt)).ToList();

        return new ActionRequired(
            PendingCertificates: pendingCerts,
            PendingReports: pendingReports,
            PendingPayments: pendingPayments,
            RecentBookings: summaries);
    }
}
