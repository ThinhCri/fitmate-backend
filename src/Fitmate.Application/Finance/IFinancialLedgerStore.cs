using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Domain.Finance;

namespace Fitmate.Application.Finance;

public record LedgerDimensionBalances(long GrossBalanceVnd, long PlatformFeeBalanceVnd, long TrainerPayableBalanceVnd);

public interface IFinancialLedgerStore
{
    Task AddEntriesAsync(IEnumerable<FinancialLedgerEntry> entries, CancellationToken ct);
    Task<bool> HasEntriesForAppointmentAsync(Guid appointmentId, CancellationToken ct);
    Task<bool> HasEntriesForRefundAsync(Guid refundId, CancellationToken ct);
    Task<bool> HasEntriesForSettlementAsync(Guid settlementId, CancellationToken ct);
    Task<IReadOnlyList<FinancialLedgerEntry>> ListEntriesPagedAsync(
        string? entryType,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct);
    Task<LedgerDimensionBalances> GetBalancesAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct);
    Task<long> GetDebitCreditSumAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
