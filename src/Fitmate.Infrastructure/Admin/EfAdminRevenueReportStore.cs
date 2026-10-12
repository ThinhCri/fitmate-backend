using Fitmate.Application.Admin;
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
        // Group each at its own event timestamp; never treat a paid but unfinished booking as revenue.
        var collections = await db.Payments.AsNoTracking()
            .Where(p => p.CompletedAt >= fromUtc && p.CompletedAt < toUtc)
            .GroupBy(p => p.CompletedAt!.Value.AddHours(7).Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(p => p.AmountVnd) }).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == RefundStatus.Succeeded &&
                r.SucceededAtUtc >= fromUtc && r.SucceededAtUtc < toUtc)
            .GroupBy(r => r.SucceededAtUtc!.Value.AddHours(7).Date)
            .Select(g => new { Date = g.Key, Amount = g.Sum(r => r.AmountVnd) }).ToListAsync(ct);
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
        foreach (var row in refunds)
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
        var result = days.Values.ToArray();
        return new RevenueReport(range, fromUtc, toUtc,
            result.Sum(d => d.CollectedVnd), result.Sum(d => d.RefundedVnd),
            result.Sum(d => d.PlatformFeeVnd), result.Sum(d => d.TrainerShareVnd),
            result.Sum(d => d.PaidOutVnd), result);
    }
}
