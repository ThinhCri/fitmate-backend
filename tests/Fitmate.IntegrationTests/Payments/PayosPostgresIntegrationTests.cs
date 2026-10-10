using System.Text.Json;
using Fitmate.Application.Outbox;
using Fitmate.Application.Booking;
using Fitmate.Application.Finance;
using Fitmate.Application.Payments;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using Fitmate.Infrastructure.Payments.Payos;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fitmate.IntegrationTests.Payments;

// No real provider calls. Uses the isolated PostgreSQL database from the existing fixture.
public class PayosPostgresIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;
    private readonly PayosOptions _options = new()
    {
        Enabled = true, MerchantApproved = true, ClientId = "test-client", ApiKey = "test-key",
        ChecksumKey = "test-checksum-not-a-live-secret", AccountNumber = "00000000",
        ReturnUrl = "https://example.test/return", CancelUrl = "https://example.test/cancel"
    };
    public PayosPostgresIntegrationTests(FitmateTestAppFactory factory)
    { _factory = factory; factory.EnsureDatabaseInitialized(); }

    private async Task<(Guid PaymentId, long OrderCode)> SeedAsync(bool expired = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var member = Guid.NewGuid(); var pt = Guid.NewGuid(); var service = Guid.NewGuid(); var time = DateTime.UtcNow.AddHours(2);
        var hold = BookingHold.Create(member, pt, service, time, TimeSpan.FromMinutes(5));
        if (expired) hold.Release();
        var appt = Appointment.CreateHold(member, pt, service, time, time, 60, 100000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 100000, null);
        var order = PayosOrder.Create(payment.Id, hold.ExpiresAt);
        db.AddRange(hold, appt, payment, order);
        await db.SaveChangesAsync();
        return (payment.Id, order.OrderCode);
    }

    private async Task<(Guid PackageId, Guid PaymentId, long OrderCode)> SeedPackageAsync(bool releaseOneHold = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var member = Guid.NewGuid(); var pt = Guid.NewGuid(); var service = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var pairs = new List<(BookingHold Hold, Appointment Appointment)>();
        for (var i = 0; i < 4; i++)
        {
            var time = now.AddHours(2 + i * 24);
            pairs.Add((BookingHold.Create(member, pt, service, time, TimeSpan.FromMinutes(10)),
                Appointment.CreateHold(member, pt, service, time, time, 60, 100000)));
        }
        var package = SessionPackage.CreateHold(member, pt, service, 100000,
            pairs.Select(p => (p.Hold.Id, p.Appointment.Id, p.Appointment.SlotStartUtc)).ToArray(),
            now, TimeSpan.FromMinutes(10));
        if (releaseOneHold) pairs[0].Hold.Release();
        db.Add(package);
        foreach (var pair in pairs) db.AddRange(pair.Hold, pair.Appointment);
        await db.SaveChangesAsync();
        var payment = Payment.CreatePendingForPackage(package.Id, PaymentMethod.Payos, package.TotalPriceVnd);
        package.AttachPayment(payment.Id, now);
        var order = PayosOrder.Create(payment.Id, package.HoldExpiresAtUtc);
        db.AddRange(payment, order);
        await db.SaveChangesAsync();
        return (package.Id, payment.Id, order.OrderCode);
    }

    private JsonElement Webhook(long code, long amount = 100000, string account = "00000000", bool tamper = false)
    {
        var values = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["orderCode"] = code, ["amount"] = amount, ["accountNumber"] = account,
            ["currency"] = "VND", ["code"] = "00", ["reference"] = "test-ref-" + code,
            ["paymentLinkId"] = "123456781234123412341234567890ab"
        };
        var canonical = string.Join("&", values.Select(p => p.Key + "=" + p.Value));
        var signature = PayosSignature.Sign(canonical, _options.ChecksumKey);
        if (tamper) values["amount"] = amount + 1;
        return JsonSerializer.SerializeToElement(new { data = values, signature, success = true, code = "00" });
    }

    private async Task DeliverAsync(JsonElement body)
    {
        using var scope = _factory.Services.CreateScope();
        var handler = new PayosWebhookHandler(scope.ServiceProvider.GetRequiredService<FitmateDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPaymentStore>(), scope.ServiceProvider.GetRequiredService<IAppointmentStore>(),
            scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>(), Options.Create(_options));
        await handler.HandleAsync(body, default);
    }

    [Fact]
    public async Task Concurrent_duplicates_settle_once_without_a_payout()
    {
        var seed = await SeedAsync();
        var body = Webhook(seed.OrderCode);
        await Task.WhenAll(DeliverAsync(body), DeliverAsync(body));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var payment = await db.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        payment.Status.Should().Be(PaymentStatus.Paid);
        var appt = await db.Appointments.SingleAsync(a => a.Id == payment.AppointmentId);
        appt.Status.Should().Be(AppointmentStatus.Confirmed);
        appt.TrainerShareVnd.Should().Be(90000);
        (await db.Set<ManualPtSettlement>().AnyAsync(s => s.AppointmentId == appt.Id)).Should().BeFalse();
        (await db.Refunds.CountAsync(r => r.PaymentId == payment.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Late_payment_creates_one_pending_manual_refund_without_restoring_booking()
    {
        var seed = await SeedAsync(expired: true);
        await DeliverAsync(Webhook(seed.OrderCode));
        await DeliverAsync(Webhook(seed.OrderCode));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var payment = await db.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        payment.Status.Should().Be(PaymentStatus.Paid);
        (await db.Refunds.SingleAsync(r => r.PaymentId == payment.Id)).Status.Should().Be(RefundStatus.Pending);
        (await db.Appointments.SingleAsync(a => a.Id == payment.AppointmentId)).PaymentId.Should().BeNull();
    }

    [Fact]
    public async Task Package_webhook_confirms_all_four_allocations_once()
    {
        var seed = await SeedPackageAsync();
        var body = Webhook(seed.OrderCode, 400000);
        await Task.WhenAll(DeliverAsync(body), DeliverAsync(body));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var package = await db.SessionPackages.Include(p => p.Sessions).SingleAsync(p => p.Id == seed.PackageId);
        package.Status.Should().Be(SessionPackageStatus.Active);
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).Status.Should().Be(PaymentStatus.Paid);
        var ids = package.Sessions.Select(s => s.AppointmentId).ToArray();
        var bookings = await db.Appointments.Where(a => ids.Contains(a.Id)).ToListAsync();
        bookings.Should().HaveCount(4);
        bookings.Should().OnlyContain(a => a.Status == AppointmentStatus.Confirmed &&
            a.PaymentId == seed.PaymentId && a.ActualPaidVnd == 100000 &&
            a.PlatformFeeVnd == 10000 && a.TrainerShareVnd == 90000);
        (await db.Refunds.CountAsync(r => r.PaymentId == seed.PaymentId)).Should().Be(0);
    }

    [Fact]
    public async Task Package_refund_projects_only_to_its_session_not_every_session()
    {
        var seed = await SeedPackageAsync();
        await DeliverAsync(Webhook(seed.OrderCode, 400000));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var package = await db.SessionPackages.Include(p => p.Sessions).SingleAsync(p => p.Id == seed.PackageId);
        var session = package.Sessions.OrderBy(s => s.Sequence).Skip(1).First();
        var payment = await db.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        var refund = Refund.CreateSystemRefund(payment.Id, session.AppointmentId, 25000, "Test package session refund");
        refund.MarkSucceeded("test-provider-" + Guid.NewGuid());
        payment.ApplyRefund(refund.AmountVnd);
        db.Refunds.Add(refund);
        await db.SaveChangesAsync();

        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);
        var adminRows = await scope.ServiceProvider.GetRequiredService<IAdminFinanceStore>()
            .ListBookingsInRangeAsync(from, to, default);
        var trainerRows = await scope.ServiceProvider.GetRequiredService<ITrainerRevenueStore>()
            .ListBookingsInRangeAsync(package.PtUserId, from, to, default);
        adminRows.Where(r => package.Sessions.Any(s => s.AppointmentId == r.AppointmentId))
            .Sum(r => r.RefundedVnd).Should().Be(25000);
        trainerRows.Single(r => r.AppointmentId == session.AppointmentId).RefundedVnd.Should().Be(25000);
        trainerRows.Where(r => r.AppointmentId != session.AppointmentId)
            .Should().OnlyContain(r => r.RefundedVnd == 0);
    }

    [Fact]
    public async Task Package_payout_checks_exact_session_allocation_and_refund()
    {
        var seed = await SeedPackageAsync();
        await DeliverAsync(Webhook(seed.OrderCode, 400000));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var package = await db.SessionPackages.Include(p => p.Sessions).SingleAsync(p => p.Id == seed.PackageId);
        var sessions = package.Sessions.OrderBy(s => s.Sequence).Take(2).ToArray();
        var pastSlot = DateTime.UtcNow.AddDays(-2);
        foreach (var session in sessions)
        {
            var appointment = await db.Appointments.SingleAsync(a => a.Id == session.AppointmentId);
            appointment.RescheduleSlot(pastSlot, pastSlot, 60);
            appointment.ForceStatusForTesting(AppointmentStatus.Completed, DateTime.UtcNow.AddHours(-25));
        }
        var payment = await db.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        var refund = Refund.CreateSystemRefund(payment.Id, sessions[1].AppointmentId, 25000, "Test package session refund");
        refund.MarkSucceeded("test-provider-" + Guid.NewGuid());
        payment.ApplyRefund(25000);
        db.Refunds.Add(refund);
        await db.SaveChangesAsync();

        var settlementStore = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        var admin = Guid.NewGuid();
        async Task<long> EligibleAsync(Guid appointmentId)
        {
            long amount = 0;
            await settlementStore.UpdateAsync(appointmentId, admin, "test.package_payout_eligibility",
                context => amount = ManualSettlementService.EligibleAmount(context), default);
            return amount;
        }
        (await EligibleAsync(sessions[0].AppointmentId)).Should().Be(90000);
        (await EligibleAsync(sessions[1].AppointmentId)).Should().Be(67500);
    }

    [Fact]
    public async Task Package_future_session_refund_cancels_only_that_session_and_never_reverses_unearned_revenue()
    {
        var seed = await SeedPackageAsync();
        await DeliverAsync(Webhook(seed.OrderCode, 400000));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var package = await db.SessionPackages.Include(p => p.Sessions).SingleAsync(p => p.Id == seed.PackageId);
        var session = package.Sessions.OrderBy(s => s.Sequence).Last();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        var admin = Guid.NewGuid();
        var nearSession = package.Sessions.OrderBy(s => s.Sequence).First();
        var nearRefund = () => store.CreateRefundAsync(admin,
            new(seed.PaymentId, nearSession.PaidAllocationVnd,
                "Near-start package session needs incident review", nearSession.AppointmentId), default);
        await nearRefund.Should().ThrowAsync<ConflictException>();
        var request = new CreateRefundRequest(seed.PaymentId, session.PaidAllocationVnd,
            "Approved future package session cancellation", session.AppointmentId);
        var refund = await store.CreateRefundAsync(admin, request, default);
        (await store.CreateRefundAsync(admin, request, default)).Id.Should().Be(refund.Id);
        (await db.Appointments.SingleAsync(a => a.Id == session.AppointmentId)).Status.Should().Be(AppointmentStatus.Cancelled);
        var anotherId = package.Sessions.OrderBy(s => s.Sequence).First().AppointmentId;
        (await db.Appointments.SingleAsync(a => a.Id == anotherId)).Status.Should().Be(AppointmentStatus.Confirmed);
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).RefundedSoFarVnd.Should().Be(0);

        await store.ApproveRefundAsync(admin, refund.Id, default);
        var transfer = new ConfirmManualRefundRequest("test-package-session-" + Guid.NewGuid(),
            "test:receipt", DateTime.UtcNow.AddSeconds(1));
        await store.ConfirmRefundAsync(admin, refund.Id, transfer, default);
        await store.ConfirmRefundAsync(admin, refund.Id, transfer, default);
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).RefundedSoFarVnd.Should().Be(100000);
        (await db.FinancialLedgerEntries.CountAsync(e => e.RefundId == refund.Id)).Should().Be(0);
        var excessive = () => store.CreateRefundAsync(admin,
            new(seed.PaymentId, 1, "Exceeds cancelled session allocation", session.AppointmentId), default);
        await excessive.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Package_completed_session_partial_refund_reverses_only_its_own_recognized_allocation()
    {
        var seed = await SeedPackageAsync();
        await DeliverAsync(Webhook(seed.OrderCode, 400000));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var package = await db.SessionPackages.Include(p => p.Sessions).SingleAsync(p => p.Id == seed.PackageId);
        var session = package.Sessions.OrderBy(s => s.Sequence).First();
        var appt = await db.Appointments.SingleAsync(a => a.Id == session.AppointmentId);
        var past = DateTime.UtcNow.AddDays(-2);
        appt.RescheduleSlot(past, past, 60);
        appt.ForceStatusForTesting(AppointmentStatus.Completed, past.AddHours(1));
        db.FinancialLedgerEntries.AddRange(new[]
        {
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, 100000, past,
                appt.PtUserId.ToString(), appointmentId: appt.Id, paymentId: seed.PaymentId, sourceEventId: $"test.completed:{appt.Id}"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, 10000, past,
                appt.PtUserId.ToString(), appointmentId: appt.Id, paymentId: seed.PaymentId, sourceEventId: $"test.completed:{appt.Id}"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, 90000, past,
                appt.PtUserId.ToString(), appointmentId: appt.Id, paymentId: seed.PaymentId, sourceEventId: $"test.completed:{appt.Id}")
        });
        await db.SaveChangesAsync();

        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        var admin = Guid.NewGuid();
        var refund = await store.CreateRefundAsync(admin,
            new(seed.PaymentId, 25000, "Reviewed completed package session", appt.Id), default);
        await store.ApproveRefundAsync(admin, refund.Id, default);
        var transfer = new ConfirmManualRefundRequest("test-completed-session-" + Guid.NewGuid(),
            "test:receipt", DateTime.UtcNow.AddSeconds(1));
        await store.ConfirmRefundAsync(admin, refund.Id, transfer, default);
        await store.ConfirmRefundAsync(admin, refund.Id, transfer, default);
        var adjustments = await db.FinancialLedgerEntries.Where(e => e.RefundId == refund.Id).ToListAsync();
        adjustments.Should().HaveCount(3);
        adjustments.Single(e => e.EntryType == FinancialLedgerEntryTypes.RefundGrossReduction).AmountVnd.Should().Be(-25000);
        adjustments.Single(e => e.EntryType == FinancialLedgerEntryTypes.RefundPlatformFeeReturn).AmountVnd.Should().Be(-2500);
        adjustments.Single(e => e.EntryType == FinancialLedgerEntryTypes.RefundTrainerDeduction).AmountVnd.Should().Be(-22500);
        adjustments.Should().OnlyContain(e => e.AppointmentId == appt.Id && e.PaymentId == seed.PaymentId);
        var excessive = () => store.CreateRefundAsync(admin,
            new(seed.PaymentId, 80000, "Would exceed one session allocation", appt.Id), default);
        (await excessive.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("REFUND_CAP_EXCEEDED");
    }

    [Fact]
    public async Task Late_package_webhook_releases_every_slot_and_creates_one_refundable_liability()
    {
        var seed = await SeedPackageAsync(releaseOneHold: true);
        var body = Webhook(seed.OrderCode, 400000);
        await DeliverAsync(body);
        await DeliverAsync(body);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var package = await db.SessionPackages.Include(p => p.Sessions).SingleAsync(p => p.Id == seed.PackageId);
        package.Status.Should().Be(SessionPackageStatus.LatePaymentRefundPending);
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).Status.Should().Be(PaymentStatus.Paid);
        var ids = package.Sessions.Select(s => s.AppointmentId).ToArray();
        var holdIds = package.Sessions.Select(s => s.HoldId).ToArray();
        (await db.Appointments.Where(a => ids.Contains(a.Id)).ToListAsync())
            .Should().OnlyContain(a => a.Status == AppointmentStatus.Expired && a.PaymentId == null);
        (await db.BookingHolds.Where(h => holdIds.Contains(h.Id)).ToListAsync())
            .Should().OnlyContain(h => h.Status == "released");
        var refund = await db.Refunds.SingleAsync(r => r.PaymentId == seed.PaymentId);
        refund.AmountVnd.Should().Be(400000);
        refund.Status.Should().Be(RefundStatus.Pending);
        var settlement = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        var admin = Guid.NewGuid();
        await settlement.ApproveRefundAsync(admin, refund.Id, default);
        await settlement.ConfirmRefundAsync(admin, refund.Id,
            new ConfirmManualRefundRequest("test-package-refund-" + Guid.NewGuid(), "test:receipt", DateTime.UtcNow.AddSeconds(1)), default);
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).Status.Should().Be(PaymentStatus.Refunded);
        (await db.FinancialLedgerEntries.CountAsync(e => e.PaymentId == seed.PaymentId)).Should().Be(0);
        var trainerRows = await scope.ServiceProvider.GetRequiredService<ITrainerRevenueStore>()
            .ListBookingsInRangeAsync(package.PtUserId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), default);
        trainerRows.Where(r => ids.Contains(r.AppointmentId)).Should().OnlyContain(r => r.RefundedVnd == 0);
    }

    [Fact]
    public async Task Package_payment_discrepancy_reports_package_id_without_a_fake_appointment()
    {
        var seed = await SeedPackageAsync();
        await DeliverAsync(Webhook(seed.OrderCode, 400000));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payments SET \"RefundedSoFarVnd\" = 1 WHERE id = {seed.PaymentId}");
            var result = await scope.ServiceProvider.GetRequiredService<IAdminFinanceStore>()
                .GetReconciliationDataAsync(default);
            var discrepancy = result.Discrepancies.Single(d => d.PaymentId == seed.PaymentId);
            discrepancy.PackageId.Should().Be(seed.PackageId);
            discrepancy.AppointmentId.Should().BeNull();
            discrepancy.ExpectedVnd.Should().Be(0);
            discrepancy.ActualVnd.Should().Be(1);
            await tx.RollbackAsync();
        });
    }

    [Theory]
    [InlineData(100001, "00000000", false)]
    [InlineData(100000, "99999999", false)]
    [InlineData(100000, "00000000", true)]
    public async Task Invalid_amount_account_or_signature_never_changes_payment(long amount, string account, bool tamper)
    {
        var seed = await SeedAsync();
        var act = () => DeliverAsync(Webhook(seed.OrderCode, amount, account, tamper));
        await act.Should().ThrowAsync<DomainException>();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).Status.Should().Be(PaymentStatus.Pending);
    }

    private async Task<(Guid AppointmentId, Guid PaymentId)> SeedCompletedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var time = DateTime.UtcNow.AddDays(-1);
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), time, time, 60, 100000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 100000, null);
        payment.MarkPaid("test-completed-" + Guid.NewGuid());
        appt.AttachPayment(payment.Id, 100000, 0, .1m, 10000, 90000, "v1");
        appt.ForceStatusForTesting(AppointmentStatus.Completed, DateTime.UtcNow.AddHours(-25));
        db.AddRange(appt, payment);
        await db.SaveChangesAsync();
        return (appt.Id, payment.Id);
    }

    private async Task ApproveAsync(Guid id, Guid admin)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
        await store.UpdateAsync(id, admin, "finance.settlement_approved", c => c.Settlement.Approve(admin,
            ManualSettlementService.EligibleAmount(c), DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(-4),
            c.Appointment.SlotStartUtc.AddMinutes(c.Appointment.DurationMinutes), "test:session", "test:payee", true, DateTime.UtcNow), default);
    }

    [Fact]
    public async Task Concurrent_manual_receipts_record_one_payout_and_preserve_approval()
    {
        var seed = await SeedCompletedAsync();
        var admin = Guid.NewGuid();
        await ApproveAsync(seed.AppointmentId, admin);
        var time = DateTime.UtcNow;
        var reference = "test-bank-" + Guid.NewGuid();
        async Task RecordAsync()
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IManualSettlementStore>().UpdateAsync(seed.AppointmentId, admin,
                "finance.trainer_settled", c => c.Settlement.RecordTransfer(admin, 90000, reference, "test:receipt", time, DateTime.UtcNow), default);
        }
        await Task.WhenAll(RecordAsync(), RecordAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var record = await db.Set<ManualPtSettlement>().SingleAsync(s => s.AppointmentId == seed.AppointmentId);
        record.Status.Should().Be("paid");
        record.AmountVnd.Should().Be(90000);
        record.CustomerConfirmedAt.Should().NotBeNull();
        (await db.AuditLogs.CountAsync(a => a.TargetId == seed.AppointmentId && a.Action == "finance.trainer_settled")).Should().Be(1);
    }

    [Fact]
    public async Task Refund_intent_holds_approved_payout_and_only_receipt_changes_money()
    {
        var seed = await SeedCompletedAsync();
        var admin = Guid.NewGuid();
        await ApproveAsync(seed.AppointmentId, admin);
        Guid refundId;
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
            var refund = await store.CreateRefundAsync(admin, new(seed.PaymentId, 10000, "test refund"), default);
            refund.Status.Should().Be(RefundStatus.Pending);
            refundId = refund.Id;
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).RefundedSoFarVnd.Should().Be(0);
            (await db.Set<ManualPtSettlement>().SingleAsync(s => s.AppointmentId == seed.AppointmentId)).Status.Should().Be("on_hold");
            var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
            var act = () => store.UpdateAsync(seed.AppointmentId, admin, "finance.trainer_settled", c =>
                c.Settlement.RecordTransfer(admin, ManualSettlementService.EligibleAmount(c), "test-bank", "test:receipt", DateTime.UtcNow, DateTime.UtcNow), default);
            await act.Should().ThrowAsync<ConflictException>();
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IManualSettlementStore>();
            await store.ApproveRefundAsync(admin, refundId, default);
            var request = new ConfirmManualRefundRequest("test-refund-" + Guid.NewGuid(), "test:receipt", DateTime.UtcNow);
            await store.ConfirmRefundAsync(admin, refundId, request, default);
            await store.ConfirmRefundAsync(admin, refundId, request, default);
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            (await db.Payments.SingleAsync(p => p.Id == seed.PaymentId)).RefundedSoFarVnd.Should().Be(10000);
            var appointment = await db.Appointments.SingleAsync(a => a.Id == seed.AppointmentId);
            var trainerRows = await scope.ServiceProvider.GetRequiredService<ITrainerRevenueStore>()
                .ListBookingsInRangeAsync(appointment.PtUserId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), default);
            trainerRows.Single(r => r.AppointmentId == appointment.Id).RefundedVnd.Should().Be(10000);
            var adminRows = await scope.ServiceProvider.GetRequiredService<IAdminFinanceStore>()
                .ListBookingsInRangeAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), default);
            adminRows.Single(r => r.AppointmentId == appointment.Id).RefundedVnd.Should().Be(10000);
        }
    }

    [Fact]
    public async Task Checkout_retry_uses_reserved_order_and_recovers_existing_link_without_scaling_vnd()
    {
        var seed = await SeedAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var posts = 0; var gets = 0;
        var handler = new FakeHttp(async request =>
        {
            request.RequestUri!.Host.Should().Be("api-merchant.payos.vn");
            request.Headers.GetValues("x-client-id").Single().Should().Be(_options.ClientId);
            request.Headers.GetValues("x-api-key").Single().Should().Be(_options.ApiKey);
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                body.RootElement.GetProperty("orderCode").GetInt64().Should().Be(seed.OrderCode);
                body.RootElement.GetProperty("amount").GetInt64().Should().Be(100000);
                var canonical = $"amount=100000&cancelUrl={_options.CancelUrl}&description=FITMATE&orderCode={seed.OrderCode}&returnUrl={_options.ReturnUrl}";
                body.RootElement.GetProperty("signature").GetString().Should().Be(PayosSignature.Sign(canonical, _options.ChecksumKey));
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"code\":\"231\"}") };
            }
            gets++;
            request.RequestUri.AbsolutePath.Should().EndWith("/" + seed.OrderCode);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { code = "00", data = new {
                    id = "123456781234123412341234567890ab", orderCode = seed.OrderCode, amount = 100000, status = "PENDING" } }))
            };
        });
        using var client = new HttpClient(handler);
        var adapter = new PayosPaymentAdapter(client, db, Options.Create(_options));
        var result = await adapter.CreatePaymentAsync(new(seed.PaymentId, 100000, "FITMATE"), default);
        await db.SaveChangesAsync();
        result.RedirectUrl.Should().Be("https://pay.payos.vn/web/123456781234123412341234567890ab");
        posts.Should().Be(1); gets.Should().Be(1);
        (await db.Set<PayosOrder>().CountAsync(o => o.PaymentId == seed.PaymentId)).Should().Be(1);
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
