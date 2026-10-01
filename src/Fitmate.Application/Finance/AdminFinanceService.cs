using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Outbox;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;

namespace Fitmate.Application.Finance;

/// <summary>
/// BE-150 & BE-809: Admin finance read-side, refunds and reconciliation.
/// Aggregate GMV, phí 10% Fitmate, payout 90% PT, refund theo kỳ; ledger entries phân trang.
/// Money invariants: GMV = Σ ActualPaid của booking Paid + Completed; phí và PT share lấy từ snapshot
/// đã lưu trên Appointment (DEC-029). KHÔNG tính lại từ rate runtime để tránh lệch lũy kế.
/// </summary>
public class AdminFinanceService : IAdminFinanceService
{
    private readonly IAdminFinanceStore _store;
    private readonly IAuthorizationGuard _authz;
    private readonly IOutboxDispatcher _outbox;
    private readonly IFinancialLedgerStore? _ledger;
    private readonly IFinanceStepUp? _stepUp;

    private const int MaxPageSize = 100;
    private const int DefaultPageSize = 50;

    public AdminFinanceService(
        IAdminFinanceStore store,
        IAuthorizationGuard authz,
        IOutboxDispatcher outbox,
        IFinancialLedgerStore? ledger = null,
        IFinanceStepUp? stepUp = null)
    {
        _store = store;
        _authz = authz;
        _outbox = outbox;
        _ledger = ledger;
        _stepUp = stepUp;
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

        // Row có type không nhận diện được (dữ liệu legacy) bị bỏ qua thay vì làm hỏng cả trang.
        var items = rows
            .Select(r => (Row: r, Parsed: ParseType(r.Type)))
            .Where(x => x.Parsed.HasValue)
            .Select(x => new LedgerEntry(
                Id: x.Row.Id,
                Type: x.Parsed!.Value,
                AmountVnd: x.Row.AmountVnd,
                AppointmentId: x.Row.AppointmentId,
                PaymentId: x.Row.PaymentId,
                OccurredAt: x.Row.OccurredAt))
            .ToList();

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

    // Type đến từ DB, không phải input người dùng: enum không nhận diện được thì
    // bỏ dòng đó khỏi kết quả thay vì ném exception (⇒ 500 cho cả request).
    private static LedgerEntryType? ParseType(string t) => t?.ToLowerInvariant() switch
    {
        "platform_fee" or "platformcommission" or "platform_commission" or "refundplatformfeereturn" or "refund_platform_fee_return" => LedgerEntryType.PlatformFee,
        "trainer_share" or "trainerpayable" or "trainer_payable" or "refundtrainerdeduction" or "refund_trainer_deduction" => LedgerEntryType.TrainerShare,
        "refund" or "refundgrossreduction" or "refund_gross_reduction" => LedgerEntryType.Refund,
        "payout" or "trainerpayout" or "trainer_payout" => LedgerEntryType.Payout,
        _ => null
    };

    public async Task<RefundView> ProcessRefundAsync(Guid adminUserId, CreateRefundRequest request, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        if (_stepUp != null)
        {
            await _stepUp.EnsureAsync(adminUserId, ct);
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });

        if (request.AmountVnd <= 0)
            throw new ValidationException(PaymentErrorCodes.InvalidAmount, "Refund amount must be positive.",
                new Dictionary<string, string[]> { ["amount"] = new[] { "must be positive" } });

        var payment = await _store.FindPaymentByIdAsync(request.PaymentId, ct)
            ?? throw new NotFoundException(PaymentErrorCodes.PaymentNotFound, $"Payment {request.PaymentId} not found.");

        if (payment.Method == PaymentMethod.Payos)
            throw new ConflictException("MANUAL_REFUND_REQUIRED", "Use manual-refunds to record the intent and then confirm a real bank transfer.");

        if (payment.Status is not (PaymentStatus.Paid or PaymentStatus.PartialRefunded))
            throw new ConflictException(ErrorCodes.Payment.AlreadyProcessed, $"Cannot refund payment in status {payment.Status}.");

        var refundedSoFarBefore = payment.RefundedSoFarVnd;
        payment.ApplyRefund(request.AmountVnd);

        var appt = await _store.FindAppointmentByIdAsync(payment.AppointmentId, ct);
        if (appt != null && payment.Status == PaymentStatus.Refunded && appt.Status != AppointmentStatus.Completed && appt.Status != AppointmentStatus.Cancelled)
        {
            appt.Cancel("Payment refunded in full");
        }

        var refund = Refund.Create(payment.Id, payment.AppointmentId, request.AmountVnd, request.Reason, adminUserId);
        refund.MarkSucceeded(null);

        await _store.AddRefundAsync(refund, ct);

        if (_ledger != null)
        {
            var platformFeeVnd = appt?.PlatformFeeVnd ?? (long)Math.Round(payment.AmountVnd * 0.10m, MidpointRounding.AwayFromZero);
            var refundedSoFarAfter = payment.RefundedSoFarVnd;
            var cumFeeBefore = payment.AmountVnd > 0 ? (long)Math.Round(platformFeeVnd * (decimal)refundedSoFarBefore / payment.AmountVnd, MidpointRounding.AwayFromZero) : 0L;
            var cumFeeAfter = payment.AmountVnd > 0 ? (long)Math.Round(platformFeeVnd * (decimal)refundedSoFarAfter / payment.AmountVnd, MidpointRounding.AwayFromZero) : 0L;
            var refundFee = cumFeeAfter - cumFeeBefore;
            var refundTrainer = request.AmountVnd - refundFee;
            var occurredAt = refund.SucceededAtUtc ?? DateTime.UtcNow;

            var adjEntries = new[]
            {
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundGrossReduction, -request.AmountVnd, occurredAt, adminUserId.ToString(), appointmentId: payment.AppointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.processed:{refund.Id}"),
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundPlatformFeeReturn, -refundFee, occurredAt, adminUserId.ToString(), appointmentId: payment.AppointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.processed:{refund.Id}"),
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundTrainerDeduction, -refundTrainer, occurredAt, adminUserId.ToString(), appointmentId: payment.AppointmentId, paymentId: payment.Id, refundId: refund.Id, sourceEventId: $"refund.processed:{refund.Id}")
            };
            await _ledger.AddEntriesAsync(adjEntries, ct);
        }

        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("finance.refund_processed", new
        {
            refundId = refund.Id,
            paymentId = payment.Id,
            appointmentId = payment.AppointmentId,
            amountVnd = request.AmountVnd,
            refundedSoFar = payment.RefundedSoFarVnd,
            totalPaid = payment.AmountVnd,
            status = payment.Status.ToString(),
            adminUserId
        });

        return new RefundView(
            refund.Id,
            refund.PaymentId,
            refund.AppointmentId,
            refund.AmountVnd,
            refund.Reason,
            refund.Status,
            refund.CreatedAt);
    }

    public async Task<ReconciliationData> GetReconciliationAsync(Guid adminUserId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        return await _store.GetReconciliationDataAsync(ct);
    }

    public async Task<Fitmate.Domain.Booking.Appointment> ReconcileLegacyAppointmentAsync(
        Guid adminUserId,
        Guid appointmentId,
        VerifyLegacyAppointmentRequest request,
        CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });

        if (string.IsNullOrWhiteSpace(request.EvidenceReference))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "EvidenceReference không được để trống.",
                new Dictionary<string, string[]> { ["evidenceReference"] = new[] { "required" } });

        if (request.VerifiedCompletedAtUtc.Kind != DateTimeKind.Utc)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "VerifiedCompletedAtUtc must be UTC.",
                new Dictionary<string, string[]> { ["verifiedCompletedAtUtc"] = new[] { "must be UTC" } });

        if (request.VerifiedCompletedAtUtc > DateTime.UtcNow)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "VerifiedCompletedAtUtc cannot be in the future.",
                new Dictionary<string, string[]> { ["verifiedCompletedAtUtc"] = new[] { "cannot be in future" } });

        var appt = await _store.FindAppointmentByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");

        if (appt.Status != AppointmentStatus.Completed)
            throw new ConflictException("APPOINTMENT_NOT_COMPLETED", "Only completed appointments can be reconciled.");

        if (request.VerifiedCompletedAtUtc < appt.SlotStartUtc)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "VerifiedCompletedAtUtc cannot be before SlotStartUtc.",
                new Dictionary<string, string[]> { ["verifiedCompletedAtUtc"] = new[] { "cannot be before slot start" } });

        appt.ReconcileLegacyCompletion(request.VerifiedCompletedAtUtc);

        if (_ledger != null && appt.ActualPaidVnd > 0 && !await _ledger.HasEntriesForAppointmentAsync(appt.Id, ct))
        {
            var entries = new[]
            {
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, appt.ActualPaidVnd, request.VerifiedCompletedAtUtc, adminUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"legacy.reconciled:{appt.Id}"),
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, appt.PlatformFeeVnd, request.VerifiedCompletedAtUtc, adminUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"legacy.reconciled:{appt.Id}"),
                FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, appt.TrainerShareVnd, request.VerifiedCompletedAtUtc, adminUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"legacy.reconciled:{appt.Id}")
            };
            await _ledger.AddEntriesAsync(entries, ct);
        }

        _outbox.Enqueue("finance.legacy_appointment_verified", new
        {
            appointmentId = appt.Id,
            verifiedCompletedAtUtc = request.VerifiedCompletedAtUtc,
            reason = request.Reason,
            evidenceReference = request.EvidenceReference,
            adminUserId
        });

        await _store.SaveChangesAsync(ct);

        return appt;
    }

    public async Task<IReadOnlyList<LegacyAppointmentReconciliationView>> GetUnverifiedLegacyAppointmentsAsync(
        Guid adminUserId,
        CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        var list = await _store.ListUnverifiedLegacyAppointmentsAsync(ct);
        return list.Select(a => new LegacyAppointmentReconciliationView(
            a.Id,
            a.MemberId,
            a.PtUserId,
            a.SlotStartUtc,
            a.ActualPaidVnd,
            a.PlatformFeeVnd,
            a.TrainerShareVnd,
            a.Status,
            a.LegacyCompletionTimeUnknown,
            a.CompletedAtUtc)).ToList();
    }
}
