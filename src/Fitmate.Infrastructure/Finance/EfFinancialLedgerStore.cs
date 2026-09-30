using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Application.Finance;
using Fitmate.Domain.Finance;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Finance;

public class EfFinancialLedgerStore : IFinancialLedgerStore
{
    private readonly FitmateDbContext _db;

    public EfFinancialLedgerStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task AddEntriesAsync(IEnumerable<FinancialLedgerEntry> entries, CancellationToken ct)
    {
        await _db.FinancialLedgerEntries.AddRangeAsync(entries, ct);
    }

    public Task<bool> HasEntriesForAppointmentAsync(Guid appointmentId, CancellationToken ct)
    {
        return _db.FinancialLedgerEntries.AnyAsync(e => e.AppointmentId == appointmentId &&
            (e.EntryType == FinancialLedgerEntryTypes.GrossBookingFee ||
             e.EntryType == FinancialLedgerEntryTypes.PlatformCommission ||
             e.EntryType == FinancialLedgerEntryTypes.TrainerPayable), ct);
    }

    public Task<bool> HasEntriesForRefundAsync(Guid refundId, CancellationToken ct)
    {
        return _db.FinancialLedgerEntries.AnyAsync(e => e.RefundId == refundId, ct);
    }

    public Task<bool> HasEntriesForSettlementAsync(Guid settlementId, CancellationToken ct)
    {
        return _db.FinancialLedgerEntries.AnyAsync(e => e.SettlementId == settlementId, ct);
    }

    public async Task<IReadOnlyList<FinancialLedgerEntry>> ListEntriesPagedAsync(
        string? entryType,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 25 : (pageSize > 100 ? 100 : pageSize);

        IQueryable<FinancialLedgerEntry> q = _db.FinancialLedgerEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entryType))
            q = q.Where(e => e.EntryType == entryType);

        if (fromUtc.HasValue)
            q = q.Where(e => e.OccurredAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            q = q.Where(e => e.OccurredAtUtc < toUtc.Value);

        return await q.OrderByDescending(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public Task<long> GetDebitCreditSumAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        IQueryable<FinancialLedgerEntry> q = _db.FinancialLedgerEntries.AsNoTracking();

        if (fromUtc.HasValue)
            q = q.Where(e => e.OccurredAtUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            q = q.Where(e => e.OccurredAtUtc < toUtc.Value);

        return q.SumAsync(e => (long?)e.AmountVnd, ct).ContinueWith(t => t.Result ?? 0L, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}
