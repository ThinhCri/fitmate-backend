using Fitmate.Application.Auth;

namespace Fitmate.Application.Finance;

/// <summary>
/// BE-150: Admin finance read-side.
/// Aggregate GMV, phí 10% Fitmate, payout 90% PT, refund theo kỳ; ledger entries phân trang.
/// Money invariants: GMV = Σ ActualPaid của booking Paid + Completed; phí và PT share lấy từ snapshot
/// đã lưu trên Appointment (DEC-029). KHÔNG tính lại từ rate runtime để tránh lệch lũy kế.
/// </summary>
public class AdminFinanceService : IAdminFinanceService
{
    private readonly IAdminFinanceStore _store;
    private readonly IAuthorizationGuard _authz;

    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 50;

    public AdminFinanceService(IAdminFinanceStore store, IAuthorizationGuard authz)
    {
        _store = store;
        _authz = authz;
    }

    public async Task<FinanceOverview> GetOverviewAsync(Guid adminUserId, FinanceRange range, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        var (fromUtc, toUtc) = ResolveRange(range);
        var bookings = await _store.ListBookingsInRangeAsync(fromUtc, toUtc, ct);

        var paidBookings = bookings.Where(b => b.IsPaid || b.IsCompleted || b.IsRefunded).ToList();

        long gmv = paidBookings.Sum(b => b.ActualPaidVnd);
        long fee = paidBookings.Sum(b => b.PlatformFeeVnd);
        long share = paidBookings.Sum(b => b.TrainerShareVnd);
        long refunded = bookings.Where(b => b.IsRefunded).Sum(b => b.RefundedVnd);

        var chart = paidBookings
            .GroupBy(b => DateOnly.FromDateTime(b.CreatedAt.AddHours(ZoneOffset.TotalHours)))
            .Select(g => new FinanceChartPoint(
                Date: g.Key,
                Bookings: g.Count(),
                PlatformFeeVnd: g.Sum(x => x.PlatformFeeVnd)))
            .OrderBy(p => p.Date)
            .ToList();

        var membersTotal = await _store.CountMembersAsync(ct);
        var activePts = await _store.CountActivePtsAsync(ct);

        return new FinanceOverview(
            Range: ToWire(range),
            MembersTotal: membersTotal,
            ActivePts: activePts,
            BookingsInPeriod: paidBookings.Count,
            GmvPaidVnd: gmv,
            PlatformFeeVnd: fee,
            TrainerShareVnd: share,
            RefundedVnd: refunded,
            Chart: chart);
    }

    public async Task<LedgerPage> GetLedgerAsync(Guid adminUserId, LedgerEntryType? type, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var skip = (page - 1) * pageSize;
        var rows = await _store.ListLedgerAsync(type, skip, pageSize, ct);
        var total = await _store.CountLedgerAsync(type, ct);

        var items = rows.Select(r => new LedgerEntry(
            Id: r.Id,
            Type: ParseType(r.Type),
            AmountVnd: r.AmountVnd,
            AppointmentId: r.AppointmentId,
            PaymentId: r.PaymentId,
            OccurredAt: r.OccurredAt)).ToList();

        return new LedgerPage(items, total, page, pageSize);
    }

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

    private static LedgerEntryType ParseType(string t) => t switch
    {
        "platform_fee" => LedgerEntryType.PlatformFee,
        "trainer_share" => LedgerEntryType.TrainerShare,
        "refund" => LedgerEntryType.Refund,
        "payout" => LedgerEntryType.Payout,
        _ => throw new InvalidOperationException($"Unknown ledger type '{t}'.")
    };
}
