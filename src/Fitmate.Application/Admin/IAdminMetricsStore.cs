using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public interface IAdminMetricsStore
{
    Task<int> CountPendingCertificatesAsync(CancellationToken ct);
    Task<int> CountPendingReportsAsync(CancellationToken ct);
    Task<int> CountPendingPaymentsAsync(CancellationToken ct);
    Task<IReadOnlyList<RecentAppointmentRow>> ListRecentAppointmentsAsync(int limit, CancellationToken ct);
}

public sealed record RecentAppointmentRow(
    Guid AppointmentId,
    Guid MemberId,
    Guid PtUserId,
    long ActualPaidVnd,
    long PlatformFeeVnd,
    BookingStatus Status,
    DateTime CreatedAt);
