using System.Text.Json.Serialization;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Finance;

public sealed record TrainerRevenueChartPoint(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("sessions_count")] int SessionsCount,
    [property: JsonPropertyName("net_earnings_vnd")] long NetEarningsVnd);

public sealed record TrainerRevenueOverview(
    [property: JsonPropertyName("range")] string Range,
    [property: JsonPropertyName("total_gross_vnd")] long TotalGrossVnd,
    [property: JsonPropertyName("platform_fee_vnd")] long PlatformFeeVnd,
    [property: JsonPropertyName("net_earnings_vnd")] long NetEarningsVnd,
    [property: JsonPropertyName("pending_payout_vnd")] long PendingPayoutVnd,
    [property: JsonPropertyName("completed_payout_vnd")] long CompletedPayoutVnd,
    [property: JsonPropertyName("completed_sessions_count")] int CompletedSessionsCount,
    [property: JsonPropertyName("chart")] IReadOnlyList<TrainerRevenueChartPoint> Chart);

public sealed record TrainerRevenueItem(
    [property: JsonPropertyName("appointment_id")] Guid AppointmentId,
    [property: JsonPropertyName("date")] DateTime Date,
    [property: JsonPropertyName("gross_vnd")] long GrossVnd,
    [property: JsonPropertyName("platform_fee_vnd")] long PlatformFeeVnd,
    [property: JsonPropertyName("net_vnd")] long NetVnd,
    [property: JsonPropertyName("status")] [property: JsonConverter(typeof(JsonStringEnumConverter))] AppointmentStatus Status,
    [property: JsonPropertyName("paid_out_vnd")] long PaidOutVnd = 0,
    [property: JsonPropertyName("settlement_status")] string SettlementStatus = "pending_review");

public sealed record TrainerRevenueHistoryPage(
    [property: JsonPropertyName("items")] IReadOnlyList<TrainerRevenueItem> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize);

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
