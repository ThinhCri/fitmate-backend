namespace Fitmate.Application.Finance;

public enum FinanceRange { _7d, _30d, _90d, All }

public enum LedgerEntryType { PlatformFee, TrainerShare, Refund, Payout }

public sealed record FinanceChartPoint(DateOnly Date, int Bookings, long PlatformFeeVnd);

public sealed record FinanceOverview(
    string Range,
    int MembersTotal,
    int ActivePts,
    int BookingsInPeriod,
    long GmvPaidVnd,
    long PlatformFeeVnd,
    long TrainerShareVnd,
    long RefundedVnd,
    IReadOnlyList<FinanceChartPoint> Chart);

public sealed record LedgerEntry(
    Guid Id,
    LedgerEntryType Type,
    long AmountVnd,
    Guid? AppointmentId,
    Guid? PaymentId,
    DateTime OccurredAt);

public sealed record LedgerPage(
    IReadOnlyList<LedgerEntry> Items,
    int Total,
    int Page,
    int PageSize);

public interface IAdminFinanceService
{
    Task<FinanceOverview> GetOverviewAsync(Guid adminUserId, FinanceRange range, CancellationToken ct);
    Task<LedgerPage> GetLedgerAsync(Guid adminUserId, LedgerEntryType? type, int page, int pageSize, CancellationToken ct);
}
