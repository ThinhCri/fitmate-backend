using Fitmate.Application.Finance;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Finance;

/// <summary>
/// EF Core impl cho finance read-side.
/// Bookings: lấy trực tiếp từ bảng Appointment + Payment.RefundedSoFarVnd (snapshot refund).
/// Ledger: project 2 entries/booking Paid: 1 platform_fee + 1 trainer_share; 1 refund entry/Refund succeeded.
/// </summary>
public class EfAdminFinanceStore : IAdminFinanceStore
{
    private readonly FitmateDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public EfAdminFinanceStore(FitmateDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<IReadOnlyList<FinanceBookingRow>> ListBookingsInRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Lấy bookings whose CreatedAt falls in [from, to) và đã paid hoặc completed hoặc refunded.
        var appts = await _db.Appointments
            .Where(a => a.CreatedAt >= fromUtc && a.CreatedAt < toUtc)
            .Where(a => a.Status == BookingStatus.Paid
                     || a.Status == BookingStatus.Completed
                     || a.Status == BookingStatus.Refunded)
            .Select(a => new
            {
                a.Id,
                a.CreatedAt,
                a.ActualPaidVnd,
                a.PlatformFeeVnd,
                a.TrainerShareVnd,
                a.Status,
                RefundedSoFar = _db.Payments
                    .Where(p => p.AppointmentId == a.Id
                             && (p.Status == PaymentStatus.Refunded || p.Status == PaymentStatus.PartialRefunded))
                    .Sum(p => (long?)p.RefundedSoFarVnd) ?? 0L
            })
            .ToListAsync(ct);

        return appts.Select(a => new FinanceBookingRow(
            AppointmentId: a.Id,
            CreatedAt: a.CreatedAt,
            ActualPaidVnd: a.ActualPaidVnd,
            PlatformFeeVnd: a.PlatformFeeVnd,
            TrainerShareVnd: a.TrainerShareVnd,
            RefundedVnd: a.RefundedSoFar,
            IsPaid: a.Status == BookingStatus.Paid,
            IsCompleted: a.Status == BookingStatus.Completed,
            IsRefunded: a.Status == BookingStatus.Refunded)).ToList();
    }

    public Task<int> CountMembersAsync(CancellationToken ct)
    {
        // Member = có role Member và KHÔNG bị lock.
        return _users.GetUsersInRoleAsync("Member").ContinueWith(t =>
            t.Result.Count(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct);
    }

    public Task<int> CountActivePtsAsync(CancellationToken ct)
    {
        // Active PT = có role PtProfile + chưa lock.
        var activePtsQuery = _users.GetUsersInRoleAsync("PT").ContinueWith(t =>
            t.Result.Count(u => u.LockoutEnd == null || u.LockoutEnd <= DateTimeOffset.UtcNow), ct);
        return activePtsQuery;
    }

    public async Task<IReadOnlyList<FinanceLedgerRow>> ListLedgerAsync(LedgerEntryType? type, int skip, int take, CancellationToken ct)
    {
        // Project 2 entries / Paid booking (platform_fee + trainer_share) và 1 entry / Refund succeeded.
        var bookings = await _db.Appointments
            .Where(a => a.Status == BookingStatus.Paid || a.Status == BookingStatus.Completed || a.Status == BookingStatus.Refunded)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.CreatedAt, a.PlatformFeeVnd, a.TrainerShareVnd, a.Status })
            .ToListAsync(ct);

        var refunds = await _db.Refunds
            .Where(r => r.Status == RefundStatus.Succeeded)
            .OrderBy(r => r.UpdatedAt)
            .Select(r => new { r.Id, r.AppointmentId, r.AmountVnd, OccurredAt = r.UpdatedAt })
            .ToListAsync(ct);

        var rows = new List<(string type, long amount, Guid id, Guid? apptId, Guid? payId, DateTime occurred)>();

        if (type is null or LedgerEntryType.PlatformFee or LedgerEntryType.TrainerShare)
        {
            foreach (var b in bookings.Where(x => x.Status == BookingStatus.Paid || x.Status == BookingStatus.Completed))
            {
                if (type is null or LedgerEntryType.PlatformFee)
                    rows.Add(("platform_fee", b.PlatformFeeVnd, b.Id, b.Id, null, b.CreatedAt));
                if (type is null or LedgerEntryType.TrainerShare)
                    rows.Add(("trainer_share", b.TrainerShareVnd, b.Id, b.Id, null, b.CreatedAt));
            }
        }
        if (type is null or LedgerEntryType.Refund)
        {
            foreach (var r in refunds)
                rows.Add(("refund", r.AmountVnd, r.Id, r.AppointmentId, null, r.OccurredAt));
        }

        // Sort by occurred desc, skip/take.
        var ordered = rows.OrderByDescending(x => x.occurred).ThenBy(x => x.id).ToList();
        var page = ordered.Skip(skip).Take(take).ToList();
        return page.Select(x => new FinanceLedgerRow(x.id, x.type, x.amount, x.apptId, x.payId, x.occurred)).ToList();
    }

    public Task<int> CountLedgerAsync(LedgerEntryType? type, CancellationToken ct)
    {
        // Đếm rows tương tự ListLedgerAsync nhưng không load toàn bộ — project counts trực tiếp.
        return CountLedgerInternalAsync(type, ct);
    }

    private async Task<int> CountLedgerInternalAsync(LedgerEntryType? type, CancellationToken ct)
    {
        int paidBookings = await _db.Appointments
            .CountAsync(a => a.Status == BookingStatus.Paid || a.Status == BookingStatus.Completed, ct);
        int succeededRefunds = await _db.Refunds.CountAsync(r => r.Status == RefundStatus.Succeeded, ct);

        return type switch
        {
            LedgerEntryType.PlatformFee => paidBookings,
            LedgerEntryType.TrainerShare => paidBookings,
            LedgerEntryType.Refund => succeededRefunds,
            LedgerEntryType.Payout => 0, // payout chưa triển khai ở BE-NNN hiện tại (UA-019)
            _ => paidBookings * 2 + succeededRefunds
        };
    }
}
