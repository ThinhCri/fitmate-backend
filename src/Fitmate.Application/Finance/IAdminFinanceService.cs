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

public sealed record CreateRefundRequest(Guid PaymentId, long AmountVnd, string Reason);

public sealed record RefundView(
    Guid Id,
    Guid PaymentId,
    Guid AppointmentId,
    long AmountVnd,
    string Reason,
    Fitmate.Domain.Common.RefundStatus Status,
    DateTime CreatedAt);

public sealed record DiscrepancyItem(
    Guid PaymentId,
    Guid AppointmentId,
    string Issue,
    long ExpectedVnd,
    long ActualVnd);

public sealed record VerifyLegacyAppointmentRequest(
    DateTime VerifiedCompletedAtUtc,
    string Reason,
    string EvidenceReference);

public sealed record LegacyAppointmentReconciliationView(
    Guid Id,
    Guid MemberId,
    Guid PtUserId,
    DateTime SlotStartUtc,
    long ActualPaidVnd,
    long PlatformFeeVnd,
    long TrainerShareVnd,
    Fitmate.Domain.Common.AppointmentStatus Status,
    bool LegacyCompletionTimeUnknown,
    DateTime? CompletedAtUtc);

public sealed record ReconciliationData(
    int TotalPaymentsCount,
    long TotalPaidVnd,
    int TotalRefundsCount,
    long TotalRefundedVnd,
    int DiscrepanciesCount,
    IReadOnlyList<DiscrepancyItem> Discrepancies);

public interface IAdminFinanceService
{
    Task<FinanceOverview> GetOverviewAsync(Guid adminUserId, FinanceRange range, CancellationToken ct);
    Task<LedgerPage> GetLedgerAsync(Guid adminUserId, LedgerEntryType? type, int page, int pageSize, CancellationToken ct);
    Task<RefundView> ProcessRefundAsync(Guid adminUserId, CreateRefundRequest request, CancellationToken ct);
    Task<ReconciliationData> GetReconciliationAsync(Guid adminUserId, CancellationToken ct);
    Task<Fitmate.Domain.Booking.Appointment> ReconcileLegacyAppointmentAsync(Guid adminUserId, Guid appointmentId, VerifyLegacyAppointmentRequest request, CancellationToken ct);
    Task<IReadOnlyList<LegacyAppointmentReconciliationView>> GetUnverifiedLegacyAppointmentsAsync(Guid adminUserId, CancellationToken ct);
}
