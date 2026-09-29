using Fitmate.Domain.Booking;

namespace Fitmate.Application.Admin;

public sealed record RecentBookingSummary(
    Guid AppointmentId,
    Guid MemberId,
    Guid PtUserId,
    long ActualPaidVnd,
    long PlatformFeeVnd,
    string Status,
    DateTime CreatedAt);

public sealed record ActionRequired(
    int PendingCertificates,
    int PendingReports,
    int PendingPayments,
    IReadOnlyList<RecentBookingSummary> RecentBookings);

public interface IAdminMetricsService
{
    Task<ActionRequired> GetActionRequiredAsync(Guid adminUserId, int recentLimit, CancellationToken ct);
}
