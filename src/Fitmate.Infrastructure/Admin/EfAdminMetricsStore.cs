using Fitmate.Application.Admin;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

/// <summary>
/// EF Core impl cho admin metrics (action-required + recent bookings).
/// Counts: thẳng từ bảng; recent bookings lấy từ Appointment theo CreatedAt desc.
/// </summary>
public class EfAdminMetricsStore : IAdminMetricsStore
{
    private readonly FitmateDbContext _db;
    public EfAdminMetricsStore(FitmateDbContext db) { _db = db; }

    public Task<int> CountPendingCertificatesAsync(CancellationToken ct)
        => _db.Certificates.CountAsync(c => c.Status == CertificateStatus.Pending, ct);

    public Task<int> CountPendingReportsAsync(CancellationToken ct)
        => _db.Reports.CountAsync(r => r.Status == ReportStatus.Pending, ct);

    public Task<int> CountPendingPaymentsAsync(CancellationToken ct)
        => _db.Payments.CountAsync(p => p.Status == PaymentStatus.Pending, ct);

    public async Task<IReadOnlyList<RecentAppointmentRow>> ListRecentAppointmentsAsync(int limit, CancellationToken ct)
    {
        var rows = await _db.Appointments
            .Where(a => a.Status == BookingStatus.Paid
                     || a.Status == BookingStatus.Completed
                     || a.Status == BookingStatus.Refunded
                     || a.Status == BookingStatus.Cancelled)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new RecentAppointmentRow(
                a.Id,
                a.MemberId,
                a.PtUserId,
                a.ActualPaidVnd,
                a.PlatformFeeVnd,
                a.Status,
                a.CreatedAt))
            .ToListAsync(ct);
        return rows;
    }
}
