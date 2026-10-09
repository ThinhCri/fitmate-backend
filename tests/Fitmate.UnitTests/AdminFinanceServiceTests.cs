using Fitmate.Application.Auth;
using Fitmate.Application.Finance;
using Fitmate.Application.Payments;
using Fitmate.Domain.Common;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-150: AdminFinanceService — overview totals + ledger paging + AuthorizationGuard.
/// </summary>
public class AdminFinanceServiceTests
{
    private sealed class RoleRequireAuthz : IAuthorizationGuard
    {
        public string? RequiredRole { get; set; }
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
        {
            if (RequiredRole != null && !string.Equals(role, RequiredRole, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Requires role {role}.");
            return Task.CompletedTask;
        }
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(RequiredRole);
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(RequiredRole != null);
    }

    private sealed class CapturingOutbox : Fitmate.Application.Outbox.IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    private sealed class InMemoryFinanceStore : IAdminFinanceStore
    {
        public List<FinanceBookingRow> Bookings { get; } = new();
        public List<FinanceLedgerRow> Ledger { get; } = new();
        public Dictionary<Guid, Fitmate.Domain.Payment.Payment> Payments { get; } = new();
        public Dictionary<Guid, Fitmate.Domain.Booking.Appointment> Appointments { get; } = new();
        public List<Fitmate.Domain.Payment.Refund> Refunds { get; } = new();
        public int MemberCount { get; set; }
        public int ActivePtCount { get; set; }

        public Task<IReadOnlyList<FinanceBookingRow>> ListBookingsInRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            var filtered = Bookings.Where(b => b.CreatedAt >= fromUtc && b.CreatedAt < toUtc).ToList();
            return Task.FromResult<IReadOnlyList<FinanceBookingRow>>(filtered);
        }
        public Task<int> CountMembersAsync(CancellationToken ct) => Task.FromResult(MemberCount);
        public Task<int> CountActivePtsAsync(CancellationToken ct) => Task.FromResult(ActivePtCount);
        public Task<IReadOnlyList<FinanceLedgerRow>> ListLedgerAsync(LedgerEntryType? type, int skip, int take, CancellationToken ct)
        {
            var rows = Ledger.Where(r => type == null || TypeMatches(r.Type, type.Value)).OrderByDescending(r => r.OccurredAt).ToList();
            return Task.FromResult<IReadOnlyList<FinanceLedgerRow>>(rows.Skip(skip).Take(take).ToList());
        }
        public Task<int> CountLedgerAsync(LedgerEntryType? type, CancellationToken ct)
            => Task.FromResult(Ledger.Count(r => type == null || TypeMatches(r.Type, type.Value)));

        public Task<Fitmate.Domain.Payment.Payment?> FindPaymentByIdAsync(Guid paymentId, CancellationToken ct)
            => Task.FromResult(Payments.GetValueOrDefault(paymentId));

        public Task<Fitmate.Domain.Booking.Appointment?> FindAppointmentByIdAsync(Guid appointmentId, CancellationToken ct)
            => Task.FromResult(Appointments.GetValueOrDefault(appointmentId));

        public Task AddRefundAsync(Fitmate.Domain.Payment.Refund refund, CancellationToken ct)
        {
            Refunds.Add(refund);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<ReconciliationData> GetReconciliationDataAsync(CancellationToken ct)
        {
            var paidPayments = Payments.Values.Where(p => p.Status is Fitmate.Domain.Common.PaymentStatus.Paid or Fitmate.Domain.Common.PaymentStatus.Refunded or Fitmate.Domain.Common.PaymentStatus.PartialRefunded).ToList();
            var succeededRefunds = Refunds.Where(r => r.Status == Fitmate.Domain.Common.RefundStatus.Succeeded).ToList();

            var discrepancies = new List<DiscrepancyItem>();
            foreach (var p in paidPayments)
            {
                var pRefunds = succeededRefunds.Where(r => r.PaymentId == p.Id).Sum(r => r.AmountVnd);
                if (p.RefundedSoFarVnd != pRefunds)
                {
                    discrepancies.Add(new DiscrepancyItem(p.Id, p.RequireAppointmentId(), "Discrepancy", pRefunds, p.RefundedSoFarVnd));
                }
            }

            return Task.FromResult(new ReconciliationData(
                paidPayments.Count,
                paidPayments.Sum(p => p.AmountVnd),
                succeededRefunds.Count,
                succeededRefunds.Sum(r => r.AmountVnd),
                discrepancies.Count,
                discrepancies));
        }

        public Task<IReadOnlyList<Fitmate.Domain.Booking.Appointment>> ListUnverifiedLegacyAppointmentsAsync(CancellationToken ct)
        {
            var unverified = Appointments.Values
                .Where(a => a.LegacyCompletionTimeUnknown && a.Status == Fitmate.Domain.Common.AppointmentStatus.Completed)
                .OrderBy(a => a.SlotStartUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<Fitmate.Domain.Booking.Appointment>>(unverified);
        }

        private static bool TypeMatches(string rowType, LedgerEntryType t) => rowType switch
        {
            "platform_fee" => t == LedgerEntryType.PlatformFee,
            "trainer_share" => t == LedgerEntryType.TrainerShare,
            "refund" => t == LedgerEntryType.Refund,
            "payout" => t == LedgerEntryType.Payout,
            _ => false
        };
    }

    private static FinanceBookingRow PaidRow(long actual, long fee, long share, DateTime utc, bool completed = false, bool refunded = false, long refundedAmount = 0)
        => new(Guid.NewGuid(), utc, actual, fee, share, refundedAmount, IsPaid: !completed && !refunded, IsCompleted: completed, IsRefunded: refunded);

    private static AdminFinanceService NewService(
        IAdminFinanceStore store,
        string? requiredRole = "Admin",
        CapturingOutbox? outbox = null,
        IFinancialLedgerStore? ledger = null,
        IFinanceStepUp? stepUp = null,
        Fitmate.Application.Trainers.IAuditLogger? audit = null)
    {
        var ledgerMock = ledger ?? new Mock<IFinancialLedgerStore>().Object;
        var stepUpMock = stepUp ?? new Mock<IFinanceStepUp>().Object;
        return new AdminFinanceService(
            store,
            new RoleRequireAuthz { RequiredRole = requiredRole },
            outbox ?? new CapturingOutbox(),
            ledgerMock,
            stepUpMock,
            audit);
    }

    [Fact]
    public async Task Overview_AggregatesGmv_Fee_Share_Refunded()
    {
        var store = new InMemoryFinanceStore { MemberCount = 10, ActivePtCount = 5 };
        var t0 = DateTime.UtcNow.AddDays(-3);
        store.Bookings.Add(PaidRow(500_000, 50_000, 450_000, t0));
        store.Bookings.Add(PaidRow(700_000, 70_000, 630_000, t0.AddHours(2), completed: true));
        store.Bookings.Add(PaidRow(1_000_000, 100_000, 900_000, t0.AddHours(4), refunded: true, refundedAmount: 1_000_000));
        var svc = NewService(store);

        var ov = await svc.GetOverviewAsync(Guid.NewGuid(), FinanceRange._30d, CancellationToken.None);
        ov.GmvPaidVnd.Should().Be(2_200_000); // 500k + 700k + 1_000_000 (Paid + Completed + Refunded cùng đã trả)
        ov.PlatformFeeVnd.Should().Be(220_000);
        ov.TrainerShareVnd.Should().Be(1_980_000);
        ov.RefundedVnd.Should().Be(1_000_000);
        ov.BookingsInPeriod.Should().Be(3);
        ov.MembersTotal.Should().Be(10);
        ov.ActivePts.Should().Be(5);
        ov.Range.Should().Be("30d");
        ov.Chart.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Overview_NonAdmin_Forbidden()
    {
        var svc = NewService(new InMemoryFinanceStore(), requiredRole: "Pt");
        var act = () => svc.GetOverviewAsync(Guid.NewGuid(), FinanceRange._30d, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Overview_Range7d_Applied()
    {
        var store = new InMemoryFinanceStore();
        var old = PaidRow(500_000, 50_000, 450_000, DateTime.UtcNow.AddDays(-100));
        var fresh = PaidRow(300_000, 30_000, 270_000, DateTime.UtcNow.AddDays(-2));
        store.Bookings.AddRange(new[] { old, fresh });
        var svc = NewService(store);

        var ov = await svc.GetOverviewAsync(Guid.NewGuid(), FinanceRange._7d, CancellationToken.None);
        ov.GmvPaidVnd.Should().Be(300_000);
        ov.BookingsInPeriod.Should().Be(1);
    }

    [Fact]
    public async Task Overview_AllRange_IncludesEverything()
    {
        var store = new InMemoryFinanceStore();
        store.Bookings.Add(PaidRow(500_000, 50_000, 450_000, DateTime.UtcNow.AddYears(-1)));
        var svc = NewService(store);

        var ov = await svc.GetOverviewAsync(Guid.NewGuid(), FinanceRange.All, CancellationToken.None);
        ov.GmvPaidVnd.Should().Be(500_000);
    }

    [Fact]
    public async Task Overview_EmptyStore_AllZeros()
    {
        var svc = NewService(new InMemoryFinanceStore());
        var ov = await svc.GetOverviewAsync(Guid.NewGuid(), FinanceRange._30d, CancellationToken.None);
        ov.GmvPaidVnd.Should().Be(0);
        ov.PlatformFeeVnd.Should().Be(0);
        ov.TrainerShareVnd.Should().Be(0);
        ov.RefundedVnd.Should().Be(0);
        ov.BookingsInPeriod.Should().Be(0);
        ov.Chart.Should().BeEmpty();
    }

    [Fact]
    public async Task Ledger_NoFilter_OrdersByOccurredDesc()
    {
        var store = new InMemoryFinanceStore();
        var t0 = DateTime.UtcNow.AddHours(-3);
        var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid(); var p3 = Guid.NewGuid();
        store.Ledger.Add(new FinanceLedgerRow(p1, "platform_fee", 50_000, p1, null, t0));
        store.Ledger.Add(new FinanceLedgerRow(p2, "trainer_share", 450_000, p2, null, t0.AddHours(1)));
        store.Ledger.Add(new FinanceLedgerRow(p3, "refund", 100_000, p3, null, t0.AddHours(2)));

        var page = await NewService(store).GetLedgerAsync(Guid.NewGuid(), type: null, page: 1, pageSize: 2, CancellationToken.None);
        page.Items.Should().HaveCount(2);
        page.Total.Should().Be(3);
        page.Items[0].Type.Should().Be(LedgerEntryType.Refund);
        page.Items[1].Type.Should().Be(LedgerEntryType.TrainerShare);
    }

    [Fact]
    public async Task Ledger_FilteredByRefund_ReturnsOnlyRefund()
    {
        var store = new InMemoryFinanceStore();
        var t0 = DateTime.UtcNow.AddHours(-1);
        store.Ledger.Add(new FinanceLedgerRow(Guid.NewGuid(), "platform_fee", 50_000, null, null, t0));
        store.Ledger.Add(new FinanceLedgerRow(Guid.NewGuid(), "refund", 50_000, null, null, t0));
        var page = await NewService(store).GetLedgerAsync(Guid.NewGuid(), LedgerEntryType.Refund, page: 1, pageSize: 50, CancellationToken.None);
        page.Items.Should().HaveCount(1).And.OnlyContain(x => x.Type == LedgerEntryType.Refund);
        page.Total.Should().Be(1);
    }

    [Fact]
    public async Task Ledger_PageSizeClampedToMax()
    {
        var store = new InMemoryFinanceStore();
        var page = await NewService(store).GetLedgerAsync(Guid.NewGuid(), null, page: 1, pageSize: 1000, CancellationToken.None);
        page.PageSize.Should().Be(100); // MaxPageSize = 100
    }

    [Fact]
    public async Task Ledger_NonAdmin_Forbidden()
    {
        var svc = NewService(new InMemoryFinanceStore(), requiredRole: "Pt");
        var act = () => svc.GetLedgerAsync(Guid.NewGuid(), null, 1, 50, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ProcessRefund_PartialRefund_UpdatesPaymentStatus_AndOutbox()
    {
        var store = new InMemoryFinanceStore();
        var outbox = new CapturingOutbox();
        var apptId = Guid.NewGuid();
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(apptId, Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        payment.MarkPaid("txn_123");
        store.Payments[payment.Id] = payment;

        var svc = NewService(store, "Admin", outbox);
        var req = new CreateRefundRequest(payment.Id, 200_000, "Customer requested partial refund");
        var result = await svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        result.AmountVnd.Should().Be(200_000);
        result.Status.Should().Be(Fitmate.Domain.Common.RefundStatus.Succeeded);
        payment.Status.Should().Be(Fitmate.Domain.Common.PaymentStatus.PartialRefunded);
        payment.RefundedSoFarVnd.Should().Be(200_000);
        store.Refunds.Should().HaveCount(1);
        outbox.Messages.Should().Contain(m => m.Type == "finance.refund_processed");
    }

    [Fact]
    public async Task ProcessRefund_FullRefund_UpdatesPayment_AndAppointmentStatus()
    {
        var store = new InMemoryFinanceStore();
        var outbox = new CapturingOutbox();
        var appt = Fitmate.Domain.Booking.Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 60, 500_000);
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(appt.Id, Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        payment.MarkPaid("txn_123");
        appt.AttachPayment(payment.Id, 500_000, 0, 0.10m, 50_000, 450_000, "v1");

        store.Payments[payment.Id] = payment;
        store.Appointments[appt.Id] = appt;

        var svc = NewService(store, "Admin", outbox);
        var req = new CreateRefundRequest(payment.Id, 500_000, "Full refund processed by admin");
        var result = await svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        result.AmountVnd.Should().Be(500_000);
        payment.Status.Should().Be(Fitmate.Domain.Common.PaymentStatus.Refunded);
        payment.RefundedSoFarVnd.Should().Be(500_000);
        appt.Status.Should().Be(Fitmate.Domain.Common.AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task ProcessRefund_ExceedsPaidAmount_ThrowsConflictException()
    {
        var store = new InMemoryFinanceStore();
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(Guid.NewGuid(), Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        payment.MarkPaid("txn_123");
        payment.ApplyRefund(400_000); // already refunded 400k
        store.Payments[payment.Id] = payment;

        var svc = NewService(store, "Admin");
        var req = new CreateRefundRequest(payment.Id, 200_000, "Refund exceeds remaining balance");
        var act = () => svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .Where(e => e.Code == ErrorCodes.Payment.RefundCapExceeded);
    }

    [Fact]
    public async Task ProcessRefund_ZeroOrNegativeAmount_ThrowsValidationException()
    {
        var store = new InMemoryFinanceStore();
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(Guid.NewGuid(), Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        payment.MarkPaid("txn_123");
        store.Payments[payment.Id] = payment;

        var svc = NewService(store, "Admin");
        var req = new CreateRefundRequest(payment.Id, 0, "Zero amount refund");
        var act = () => svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(e => e.Code == PaymentErrorCodes.InvalidAmount);
    }

    [Fact]
    public async Task ProcessRefund_ReasonTooShort_ThrowsValidationException()
    {
        var store = new InMemoryFinanceStore();
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(Guid.NewGuid(), Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        payment.MarkPaid("txn_123");
        store.Payments[payment.Id] = payment;

        var svc = NewService(store, "Admin");
        var req = new CreateRefundRequest(payment.Id, 100_000, "abc");
        var act = () => svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ProcessRefund_PaymentNotFound_ThrowsNotFoundException()
    {
        var store = new InMemoryFinanceStore();
        var svc = NewService(store, "Admin");
        var req = new CreateRefundRequest(Guid.NewGuid(), 100_000, "Payment missing");
        var act = () => svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ProcessRefund_PaymentPending_ThrowsConflictException()
    {
        var store = new InMemoryFinanceStore();
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(Guid.NewGuid(), Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        store.Payments[payment.Id] = payment;

        var svc = NewService(store, "Admin");
        var req = new CreateRefundRequest(payment.Id, 100_000, "Cannot refund pending payment");
        var act = () => svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task ProcessRefund_NonAdmin_ThrowsForbiddenException()
    {
        var store = new InMemoryFinanceStore();
        var svc = NewService(store, "Pt");
        var req = new CreateRefundRequest(Guid.NewGuid(), 100_000, "Unauthorized refund");
        var act = () => svc.ProcessRefundAsync(Guid.NewGuid(), req, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetReconciliation_ReturnsTotalsAndDiscrepancies()
    {
        var store = new InMemoryFinanceStore();
        var p1 = Fitmate.Domain.Payment.Payment.CreatePending(Guid.NewGuid(), Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        p1.MarkPaid("txn_1");
        p1.ApplyRefund(100_000);
        store.Payments[p1.Id] = p1;

        var r1 = Fitmate.Domain.Payment.Refund.Create(p1.Id, p1.RequireAppointmentId(), 100_000, "Discrepancy test", Guid.NewGuid());
        r1.MarkSucceeded("ref_1");
        store.Refunds.Add(r1);

        var svc = NewService(store, "Admin");
        var result = await svc.GetReconciliationAsync(Guid.NewGuid(), CancellationToken.None);

        result.TotalPaymentsCount.Should().Be(1);
        result.TotalPaidVnd.Should().Be(500_000);
        result.TotalRefundsCount.Should().Be(1);
        result.TotalRefundedVnd.Should().Be(100_000);
        result.DiscrepanciesCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessRefund_WithStepUp_EnforcesStepUp()
    {
        var store = new InMemoryFinanceStore();
        var outbox = new CapturingOutbox();
        var payment = Fitmate.Domain.Payment.Payment.CreatePending(Guid.NewGuid(), Fitmate.Domain.Common.PaymentMethod.Vnpay, 500_000, "url");
        payment.MarkPaid("txn_123");
        store.Payments[payment.Id] = payment;

        var stepUpMock = new Mock<IFinanceStepUp>();
        var adminId = Guid.NewGuid();
        var svc = NewService(store, requiredRole: "Admin", outbox: outbox, stepUp: stepUpMock.Object);

        var req = new CreateRefundRequest(payment.Id, 200_000, "Valid reason here");
        await svc.ProcessRefundAsync(adminId, req, CancellationToken.None);

        stepUpMock.Verify(s => s.EnsureAsync(adminId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
