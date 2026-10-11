using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Finance;

public class TrainerRevenueService : ITrainerRevenueService
{
    private readonly ITrainerRevenueStore _store;
    private readonly IAuthorizationGuard _authz;
    private readonly IOutboxDispatcher _outbox;

    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 20;

    public TrainerRevenueService(
        ITrainerRevenueStore store,
        IAuthorizationGuard authz,
        IOutboxDispatcher outbox)
    {
        _store = store;
        _authz = authz;
        _outbox = outbox;
    }

    public async Task<TrainerRevenueOverview> GetOverviewAsync(Guid ptUserId, FinanceRange range, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);

        var (fromUtc, toUtc) = ResolveRange(range);
        var rows = await _store.ListBookingsInRangeAsync(ptUserId, fromUtc, toUtc, ct);

        // Completion recognizes earnings; a verified transfer independently records paid-out money.
        var paidRows = rows.Where(r => r.Status == AppointmentStatus.Completed).ToList();

        long gross = paidRows.Sum(r => r.ActualPaidVnd);
        long fee = paidRows.Sum(r => r.PlatformFeeVnd);

        // Net earnings = trainer share - (trainer proportion of refund or total refund deducted from gross)
        long netEarnings = paidRows.Sum(NetRevenue);
        long paidOut = rows.Sum(r => r.PaidOutVnd);

        var completedRows = rows.Where(r => r.Status == AppointmentStatus.Completed).ToList();
        var completedSessionsCount = completedRows.Count;

        var chart = completedRows
            .GroupBy(b => DateOnly.FromDateTime(b.SlotStartUtc.AddHours(ZoneOffset.TotalHours)))
            .Select(g => new TrainerRevenueChartPoint(
                Date: g.Key,
                SessionsCount: g.Count(),
                NetEarningsVnd: g.Sum(NetRevenue)))
            .OrderBy(p => p.Date)
            .ToList();

        return new TrainerRevenueOverview(
            Range: ToWire(range),
            TotalGrossVnd: gross,
            PlatformFeeVnd: fee,
            NetEarningsVnd: netEarnings,
            PendingPayoutVnd: netEarnings - paidOut,
            CompletedPayoutVnd: paidOut,
            CompletedSessionsCount: completedSessionsCount,
            Chart: chart);
    }

    public async Task<TrainerRevenueHistoryPage> GetHistoryAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var skip = (page - 1) * pageSize;
        var (rows, total) = await _store.ListHistoryPagedAsync(ptUserId, skip, pageSize, ct);

        var items = rows.Select(r => new TrainerRevenueItem(
            AppointmentId: r.AppointmentId,
            Date: r.SlotStartUtc,
            GrossVnd: r.ActualPaidVnd,
            PlatformFeeVnd: r.PlatformFeeVnd,
            NetVnd: NetRevenue(r),
            Status: r.Status, PaidOutVnd: r.PaidOutVnd, SettlementStatus: r.SettlementStatus,
            TransferredAtUtc: r.TransferredAtUtc, BankReference: r.BankReference,
            PayeeBankName: r.PayeeBankName,
            PayeeAccountNumberMasked: Mask(r.PayeeAccountNumber),
            PayeeAccountName: r.PayeeAccountName)).ToList();

        return new TrainerRevenueHistoryPage(items, total, page, pageSize);
    }

    public async Task<TrainerSettlementResult> SettleTrainerAsync(Guid adminUserId, TrainerSettlementRequest request, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (request.AmountVnd <= 0)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Settlement amount must be greater than zero.",
                new Dictionary<string, string[]> { ["amount"] = new[] { "must be positive" } });

        if (string.IsNullOrWhiteSpace(request.ReferenceNumber))
            throw new ValidationException(ErrorCodes.Validation.RequiredField, "ReferenceNumber is required.",
                new Dictionary<string, string[]> { ["reference_number"] = new[] { "required" } });

        throw new ConflictException("MANUAL_SETTLEMENT_REVIEW_REQUIRED",
            "Use manual-settlements per appointment: approve the verification record, then record the actual bank transfer.");
    }

    private static long NetRevenue(TrainerBookingRevenueRow row) => row.ActualPaidVnd <= 0 ? 0 :
        row.TrainerShareVnd - row.RefundedVnd + (long)Math.Round(
            row.PlatformFeeVnd * (decimal)row.RefundedVnd / row.ActualPaidVnd, MidpointRounding.AwayFromZero);

    private static string? Mask(string? value) => string.IsNullOrEmpty(value) ? null :
        value.Length <= 4 ? value : new string('•', value.Length - 4) + value[^4..];

    private static readonly TimeSpan ZoneOffset = TimeSpan.FromHours(7); // Asia/Ho_Chi_Minh

    private static (DateTime fromUtc, DateTime toUtc) ResolveRange(FinanceRange r)
    {
        var now = DateTime.UtcNow;
        return r switch
        {
            FinanceRange._7d => (now.AddDays(-7), now),
            FinanceRange._30d => (now.AddDays(-30), now),
            FinanceRange._90d => (now.AddDays(-90), now),
            _ => (DateTime.MinValue, now)
        };
    }

    private static string ToWire(FinanceRange r) => r switch
    {
        FinanceRange._7d => "7d",
        FinanceRange._30d => "30d",
        FinanceRange._90d => "90d",
        _ => "all"
    };
}
