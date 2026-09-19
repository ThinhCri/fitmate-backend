using Fitmate.Application.Common;

namespace Fitmate.Application.Finance;

public interface IAdminFinanceStore
{
    /// <summary>Trả về các bookings trong [fromUtc, toUtc) theo trạng thái tương ứng.</summary>
    Task<IReadOnlyList<FinanceBookingRow>> ListBookingsInRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<int> CountMembersAsync(CancellationToken ct);
    Task<int> CountActivePtsAsync(CancellationToken ct);
    Task<IReadOnlyList<FinanceLedgerRow>> ListLedgerAsync(LedgerEntryType? type, int skip, int take, CancellationToken ct);
    Task<int> CountLedgerAsync(LedgerEntryType? type, CancellationToken ct);
}

/// <summary>Snapshot booking nhỏ cho finance aggregation.</summary>
public sealed record FinanceBookingRow(
    Guid AppointmentId,
    DateTime CreatedAt,
    long ActualPaidVnd,
    long PlatformFeeVnd,
    long TrainerShareVnd,
    long RefundedVnd,
    bool IsPaid,
    bool IsCompleted,
    bool IsRefunded);

/// <summary>Snapshot ledger entry thô (1 row = 1 dòng kế toán).</summary>
public sealed record FinanceLedgerRow(
    Guid Id,
    string Type,
    long AmountVnd,
    Guid? AppointmentId,
    Guid? PaymentId,
    DateTime OccurredAt);
