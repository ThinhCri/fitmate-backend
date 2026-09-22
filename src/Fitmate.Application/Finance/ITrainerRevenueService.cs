using Fitmate.Domain.Common;

namespace Fitmate.Application.Finance;

public sealed record TrainerRevenueChartPoint(DateOnly Date, int SessionsCount, long NetEarningsVnd);

public sealed record TrainerRevenueOverview(
    string Range,
    long TotalGrossVnd,
    long PlatformFeeVnd,
    long NetEarningsVnd,
    long PendingPayoutVnd,
    long CompletedPayoutVnd,
    int CompletedSessionsCount,
    IReadOnlyList<TrainerRevenueChartPoint> Chart);

public sealed record TrainerRevenueItem(
    Guid AppointmentId,
    DateTime Date,
    long GrossVnd,
    long PlatformFeeVnd,
    long NetVnd,
    BookingStatus Status);

public sealed record TrainerRevenueHistoryPage(
    IReadOnlyList<TrainerRevenueItem> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record TrainerSettlementRequest(
    Guid PtUserId,
    long AmountVnd,
    string ReferenceNumber,
    string? Note);

public sealed record TrainerSettlementResult(
    Guid SettlementId,
    Guid PtUserId,
    long AmountVnd,
    string ReferenceNumber,
    DateTime ProcessedAt);

public interface ITrainerRevenueService
{
    Task<TrainerRevenueOverview> GetOverviewAsync(Guid ptUserId, FinanceRange range, CancellationToken ct);
    Task<TrainerRevenueHistoryPage> GetHistoryAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct);
    Task<TrainerSettlementResult> SettleTrainerAsync(Guid adminUserId, TrainerSettlementRequest request, CancellationToken ct);
}

