using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-810: Kiểm thử quy trình đối soát lịch hẹn lịch sử (Legacy Appointment Reconciliation) và chặn settlement.
/// </summary>
public class LegacyAppointmentReconciliationTests
{
    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    [Fact]
    public void EligibleAmount_WhenLegacyCompletionTimeUnknown_ThrowsSettlementBlockedException()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, DateTime.UtcNow.AddHours(-3), DateTime.UtcNow.AddHours(-3), 60, 500_000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 500_000, null);
        payment.MarkPaid("TXN-1");
        appt.AttachPayment(payment.Id, 500_000, 0, 0.10m, 50_000, 450_000, "v1");
        appt.SetLegacyData(legacyStatus: 5, completedAtUtc: null, legacyCompletionTimeUnknown: true);
        appt.ForceStatusForTesting(AppointmentStatus.Completed);

        var settlement = ManualPtSettlement.Create(appt.Id, ptId, payment.Id);
        var context = new SettlementContext(appt, payment, settlement, HasPendingRefund: false);

        var act = () => ManualSettlementService.EligibleAmount(context);
        act.Should().Throw<ConflictException>()
            .Which.Code.Should().Be("SETTLEMENT_BLOCKED_LEGACY_UNKNOWN");
    }

    [Fact]
    public async Task ReconcileLegacyAppointmentAsync_WhenVerified_UpdatesTimestampAndUnblocks()
    {
        var adminId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var slotStart = DateTime.UtcNow.AddDays(-1);

        var appt = Appointment.CreateHold(memberId, ptId, serviceId, slotStart, slotStart, 60, 500_000);
        var payment = Payment.CreatePending(appt.Id, PaymentMethod.Payos, 500_000, null);
        payment.MarkPaid("TXN-2");
        appt.AttachPayment(payment.Id, 500_000, 0, 0.10m, 50_000, 450_000, "v1");
        appt.SetLegacyData(legacyStatus: 5, completedAtUtc: null, legacyCompletionTimeUnknown: true);
        appt.ForceStatusForTesting(AppointmentStatus.Completed);

        var storeMock = new Mock<IAdminFinanceStore>();
        storeMock.Setup(s => s.FindAppointmentByIdAsync(appt.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appt);

        var authzMock = new Mock<IAuthorizationGuard>();
        var outbox = new CapturingOutbox();

        var ledgerMock = new Mock<IFinancialLedgerStore>();
        ledgerMock.Setup(l => l.HasEntriesForAppointmentAsync(appt.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var addedEntries = new List<FinancialLedgerEntry>();
        ledgerMock.Setup(l => l.AddEntriesAsync(It.IsAny<IEnumerable<FinancialLedgerEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<FinancialLedgerEntry>, CancellationToken>((entries, _) => addedEntries.AddRange(entries));

        var service = new AdminFinanceService(storeMock.Object, authzMock.Object, outbox, ledgerMock.Object);

        var verifiedCompletedAtUtc = slotStart.AddHours(1); // 1 hour after slot start
        var request = new VerifyLegacyAppointmentRequest(
            VerifiedCompletedAtUtc: verifiedCompletedAtUtc,
            Reason: "Xác minh qua tin nhắn trao đổi và biên bản buổi tập",
            EvidenceReference: "EVID-ATTENDANCE-DOC-001");

        var result = await service.ReconcileLegacyAppointmentAsync(adminId, appt.Id, request, CancellationToken.None);

        result.LegacyCompletionTimeUnknown.Should().BeFalse();
        result.CompletedAtUtc.Should().Be(verifiedCompletedAtUtc);

        // Verify ledger entries created
        addedEntries.Should().HaveCount(3);
        addedEntries.Should().Contain(e => e.EntryType == FinancialLedgerEntryTypes.GrossBookingFee && e.AmountVnd == 500_000);
        addedEntries.Should().Contain(e => e.EntryType == FinancialLedgerEntryTypes.PlatformCommission && e.AmountVnd == 50_000);
        addedEntries.Should().Contain(e => e.EntryType == FinancialLedgerEntryTypes.TrainerPayable && e.AmountVnd == 450_000);

        outbox.Messages.Should().Contain(m => m.Type == "finance.legacy_appointment_verified");

        // Verify settlement is now eligible
        var settlement = ManualPtSettlement.Create(appt.Id, ptId, payment.Id);
        var context = new SettlementContext(appt, payment, settlement, HasPendingRefund: false);

        var eligibleAmount = ManualSettlementService.EligibleAmount(context);
        eligibleAmount.Should().Be(450_000);
    }

    [Fact]
    public async Task ReconcileLegacyAppointmentAsync_WhenTimestampInFuture_ThrowsValidationException()
    {
        var adminId = Guid.NewGuid();
        var appt = Appointment.CreateHold(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), 60, 500_000);
        appt.ForceStatusForTesting(AppointmentStatus.Completed);
        appt.SetLegacyData(5, null, true);

        var storeMock = new Mock<IAdminFinanceStore>();
        storeMock.Setup(s => s.FindAppointmentByIdAsync(appt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        var authzMock = new Mock<IAuthorizationGuard>();
        var outbox = new CapturingOutbox();

        var service = new AdminFinanceService(storeMock.Object, authzMock.Object, outbox);

        var request = new VerifyLegacyAppointmentRequest(
            VerifiedCompletedAtUtc: DateTime.UtcNow.AddDays(1), // in future!
            Reason: "Xác nhận hợp lệ",
            EvidenceReference: "REF-001");

        var act = () => service.ReconcileLegacyAppointmentAsync(adminId, appt.Id, request, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }
}
