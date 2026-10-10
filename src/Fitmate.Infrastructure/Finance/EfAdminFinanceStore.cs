using Fitmate.Application.Finance;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Finance;
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
            .Where(a => a.ActualPaidVnd > 0 && (a.Status == AppointmentStatus.Pending
                     || a.Status == AppointmentStatus.Confirmed
                     || a.Status == AppointmentStatus.InProgress
                     || a.Status == AppointmentStatus.AwaitingMemberConfirmation
                     || a.Status == AppointmentStatus.Disputed
                     || a.Status == AppointmentStatus.Completed
                     || a.Status == AppointmentStatus.Cancelled))
            .Select(a => new
            {
                a.Id,
                a.CreatedAt,
                a.ActualPaidVnd,
                a.PlatformFeeVnd,
                a.TrainerShareVnd,
                a.Status,
                RefundedSoFar = a.ActualPaidVnd > 0
                    ? _db.Refunds.Where(r => r.AppointmentId == a.Id && r.PaymentId == a.PaymentId &&
                        r.Status == RefundStatus.Succeeded).Sum(r => (long?)r.AmountVnd) ?? 0L
                    : 0L
            })
            .ToListAsync(ct);

        return appts.Select(a => new FinanceBookingRow(
            AppointmentId: a.Id,
            CreatedAt: a.CreatedAt,
            ActualPaidVnd: a.ActualPaidVnd,
            PlatformFeeVnd: a.PlatformFeeVnd,
            TrainerShareVnd: a.TrainerShareVnd,
            RefundedVnd: a.RefundedSoFar,
            IsPaid: a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed ||
                a.Status == AppointmentStatus.InProgress || a.Status == AppointmentStatus.AwaitingMemberConfirmation ||
                a.Status == AppointmentStatus.Disputed,
            IsCompleted: a.Status == AppointmentStatus.Completed,
            IsRefunded: a.RefundedSoFar > 0)).ToList();
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
        if (await _db.FinancialLedgerEntries.AnyAsync(ct))
        {
            var query = _db.FinancialLedgerEntries.AsNoTracking();

            if (type.HasValue)
            {
                query = type.Value switch
                {
                    LedgerEntryType.PlatformFee => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.PlatformCommission || e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn),
                    LedgerEntryType.TrainerShare => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayable || e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction),
                    LedgerEntryType.Refund => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.RefundGrossReduction || e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn || e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction),
                    LedgerEntryType.Payout => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayout),
                    _ => query
                };
            }

            var entries = await query
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenBy(e => e.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);

            return entries.Select(e => new FinanceLedgerRow(
                e.Id,
                e.EntryType switch
                {
                    FinancialLedgerEntryTypes.PlatformCommission => "platform_fee",
                    FinancialLedgerEntryTypes.TrainerPayable => "trainer_share",
                    FinancialLedgerEntryTypes.RefundGrossReduction => "refund",
                    FinancialLedgerEntryTypes.RefundPlatformFeeReturn => "platform_fee",
                    FinancialLedgerEntryTypes.RefundTrainerDeduction => "trainer_share",
                    FinancialLedgerEntryTypes.TrainerPayout => "payout",
                    _ => e.EntryType
                },
                Math.Abs(e.AmountVnd),
                e.AppointmentId,
                e.PaymentId,
                e.OccurredAtUtc)).ToList();
        }

        // Legacy fallback: only a completed booking recognizes platform/trainer revenue.
        var bookings = await _db.Appointments
            .Where(a => a.Status == AppointmentStatus.Completed || a.Status == AppointmentStatus.Cancelled)
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
            foreach (var b in bookings.Where(x => x.Status == AppointmentStatus.Completed))
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

        if (type is null or LedgerEntryType.Payout)
        {
            var paid = await _db.Set<ManualPtSettlement>().AsNoTracking().Where(s => s.Status == "paid").ToListAsync(ct);
            foreach (var s in paid)
                rows.Add(("payout", s.AmountVnd, s.AppointmentId, s.AppointmentId, s.PaymentId, s.TransferredAt!.Value));
        }

        // Sort by occurred desc, skip/take.
        var ordered = rows.OrderByDescending(x => x.occurred).ThenBy(x => x.id).ToList();
        var page = ordered.Skip(skip).Take(take).ToList();
        return page.Select(x => new FinanceLedgerRow(x.id, x.type, x.amount, x.apptId, x.payId, x.occurred)).ToList();
    }

    public async Task<int> CountLedgerAsync(LedgerEntryType? type, CancellationToken ct)
    {
        if (await _db.FinancialLedgerEntries.AnyAsync(ct))
        {
            var query = _db.FinancialLedgerEntries.AsNoTracking();

            if (type.HasValue)
            {
                query = type.Value switch
                {
                    LedgerEntryType.PlatformFee => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.PlatformCommission || e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn),
                    LedgerEntryType.TrainerShare => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayable || e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction),
                    LedgerEntryType.Refund => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.RefundGrossReduction || e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn || e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction),
                    LedgerEntryType.Payout => query.Where(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayout),
                    _ => query
                };
            }

            return await query.CountAsync(ct);
        }

        // Đếm rows tương tự ListLedgerAsync nhưng không load toàn bộ — project counts trực tiếp.
        return await CountLedgerInternalAsync(type, ct);
    }

    private async Task<int> CountLedgerInternalAsync(LedgerEntryType? type, CancellationToken ct)
    {
        int paidBookings = await _db.Appointments
            .CountAsync(a => a.ActualPaidVnd > 0 && a.Status == AppointmentStatus.Completed, ct);
        int succeededRefunds = await _db.Refunds.CountAsync(r => r.Status == RefundStatus.Succeeded, ct);
        int payouts = await _db.Set<ManualPtSettlement>().CountAsync(s => s.Status == "paid", ct);

        return type switch
        {
            LedgerEntryType.PlatformFee => paidBookings,
            LedgerEntryType.TrainerShare => paidBookings,
            LedgerEntryType.Refund => succeededRefunds,
            LedgerEntryType.Payout => payouts,
            _ => paidBookings * 2 + succeededRefunds + payouts
        };
    }

    public Task<Payment?> FindPaymentByIdAsync(Guid paymentId, CancellationToken ct)
        => _db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);

    public Task<Appointment?> FindAppointmentByIdAsync(Guid appointmentId, CancellationToken ct)
        => _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

    public Task AddRefundAsync(Refund refund, CancellationToken ct)
    {
        _db.Refunds.Add(refund);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public async Task<ReconciliationData> GetReconciliationDataAsync(CancellationToken ct)
    {
        var payments = await _db.Payments.ToListAsync(ct);
        var refunds = await _db.Refunds.ToListAsync(ct);

        var paidPayments = payments.Where(p => p.Status is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartialRefunded).ToList();
        var succeededRefunds = refunds.Where(r => r.Status == RefundStatus.Succeeded).ToList();

        var totalPaidVnd = paidPayments.Sum(p => p.AmountVnd);
        var totalRefundedVnd = succeededRefunds.Sum(r => r.AmountVnd);

        var discrepancies = new List<DiscrepancyItem>();

        foreach (var p in paidPayments)
        {
            var pRefunds = succeededRefunds.Where(r => r.PaymentId == p.Id).Sum(r => r.AmountVnd);
            if (p.RefundedSoFarVnd != pRefunds)
            {
                discrepancies.Add(new DiscrepancyItem(
                    PaymentId: p.Id,
                    AppointmentId: p.AppointmentId,
                    PackageId: p.PackageId,
                    Issue: "Payment.RefundedSoFarVnd differs from sum of succeeded Refunds",
                    ExpectedVnd: pRefunds,
                    ActualVnd: p.RefundedSoFarVnd));
            }
        }

        return new ReconciliationData(
            TotalPaymentsCount: paidPayments.Count,
            TotalPaidVnd: totalPaidVnd,
            TotalRefundsCount: succeededRefunds.Count,
            TotalRefundedVnd: totalRefundedVnd,
            DiscrepanciesCount: discrepancies.Count,
            Discrepancies: discrepancies);
    }

    public async Task<IReadOnlyList<Appointment>> ListUnverifiedLegacyAppointmentsAsync(CancellationToken ct)
    {
        return await _db.Appointments
            .Where(a => a.LegacyCompletionTimeUnknown && a.Status == AppointmentStatus.Completed)
            .OrderBy(a => a.SlotStartUtc)
            .ToListAsync(ct);
    }
}
