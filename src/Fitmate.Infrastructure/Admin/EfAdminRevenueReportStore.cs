using Fitmate.Application.Admin;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

public sealed class EfAdminRevenueReportStore(FitmateDbContext db) : IAdminRevenueReportStore
{
    public async Task<RevenueReport> GetAsync(string range, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct)
    {
        // Payment collection, actual bank refund and earned platform fee happen at different times.
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Paid &&
                (p.CompletedAt ?? p.CreatedAt) >= fromUtc && (p.CompletedAt ?? p.CreatedAt) < toUtc)
            .Select(p => new
            {
                Date = (p.CompletedAt ?? p.CreatedAt).AddHours(7).Date,
                p.AmountVnd
            })
            .ToListAsync(ct);
        var collections = payments
            .GroupBy(p => p.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(p => p.AmountVnd) })
            .ToList();

        var refunds = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Succeeded &&
                (r.SucceededAtUtc ?? r.CreatedAt) >= fromUtc && (r.SucceededAtUtc ?? r.CreatedAt) < toUtc)
            .Select(r => new
            {
                Date = (r.SucceededAtUtc ?? r.CreatedAt).AddHours(7).Date,
                r.AmountVnd
            })
            .ToListAsync(ct);
        var groupedRefunds = refunds
            .GroupBy(r => r.Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.AmountVnd) })
            .ToList();

        var ledger = await db.FinancialLedgerEntries.AsNoTracking()
            .Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc < toUtc &&
                (e.EntryType == FinancialLedgerEntryTypes.PlatformCommission ||
                 e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn ||
                 e.EntryType == FinancialLedgerEntryTypes.TrainerPayable ||
                 e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction ||
                 e.EntryType == FinancialLedgerEntryTypes.TrainerPayout))
            .GroupBy(e => new { Date = e.OccurredAtUtc.AddHours(7).Date, e.EntryType })
            .Select(g => new { g.Key.Date, g.Key.EntryType, Amount = g.Sum(e => e.AmountVnd) })
            .ToListAsync(ct);

        var days = new SortedDictionary<DateOnly, RevenueDay>();
        RevenueDay Current(DateTime value)
        {
            var date = DateOnly.FromDateTime(value);
            if (!days.TryGetValue(date, out var day))
                days[date] = day = new RevenueDay(date, 0, 0, 0, 0, 0);
            return day;
        }
        foreach (var row in collections)
        {
            var day = Current(row.Date);
            days[day.Date] = day with { CollectedVnd = row.Amount };
        }
        foreach (var row in groupedRefunds)
        {
            var day = Current(row.Date);
            days[day.Date] = day with { RefundedVnd = row.Amount };
        }
        foreach (var row in ledger)
        {
            var day = Current(row.Date);
            days[day.Date] = row.EntryType switch
            {
                FinancialLedgerEntryTypes.PlatformCommission or FinancialLedgerEntryTypes.RefundPlatformFeeReturn
                    => day with { PlatformFeeVnd = day.PlatformFeeVnd + row.Amount },
                FinancialLedgerEntryTypes.TrainerPayable or FinancialLedgerEntryTypes.RefundTrainerDeduction
                    => day with { TrainerShareVnd = day.TrainerShareVnd + row.Amount },
                FinancialLedgerEntryTypes.TrainerPayout
                    => day with { PaidOutVnd = day.PaidOutVnd - row.Amount },
                _ => day
            };
        }

        // Fallback: If financial ledger entries have not recorded platform fee yet,
        // compute from completed/confirmed appointments directly so admin sees accurate revenue.
        var totalFeeInDays = days.Values.Sum(d => d.PlatformFeeVnd);
        if (totalFeeInDays == 0)
        {
            var appts = await db.Appointments.AsNoTracking()
                .Where(a => (a.CompletedAtUtc ?? a.ConfirmedAtUtc ?? a.CreatedAt) >= fromUtc &&
                            (a.CompletedAtUtc ?? a.ConfirmedAtUtc ?? a.CreatedAt) < toUtc &&
                            (a.Status == AppointmentStatus.Completed ||
                             a.Status == AppointmentStatus.Confirmed ||
                             a.Status == AppointmentStatus.InProgress ||
                             a.Status == AppointmentStatus.AwaitingMemberConfirmation ||
                             a.ActualPaidVnd > 0))
                .Select(a => new
                {
                    Date = (a.CompletedAtUtc ?? a.ConfirmedAtUtc ?? a.CreatedAt).AddHours(7).Date,
                    a.PlatformFeeVnd,
                    a.TrainerShareVnd,
                    a.ActualPaidVnd
                })
                .ToListAsync(ct);

            foreach (var a in appts)
            {
                var day = Current(a.Date);
                days[day.Date] = day with
                {
                    CollectedVnd = day.CollectedVnd > 0 ? day.CollectedVnd : day.CollectedVnd + a.ActualPaidVnd,
                    PlatformFeeVnd = day.PlatformFeeVnd + a.PlatformFeeVnd,
                    TrainerShareVnd = day.TrainerShareVnd + a.TrainerShareVnd
                };
            }
        }
        var result = days.Values.ToArray();
        return new RevenueReport(range, fromUtc, toUtc,
            result.Sum(d => d.CollectedVnd), result.Sum(d => d.RefundedVnd),
            result.Sum(d => d.PlatformFeeVnd), result.Sum(d => d.TrainerShareVnd),
            result.Sum(d => d.PaidOutVnd), result);
    }
}
