using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-810: Unit tests cho Sổ cái tài chính bất biến (Immutable Append-Only Financial Ledger).
/// </summary>
public class FinancialLedgerTests
{
    [Fact]
    public void FinancialLedgerEntry_CreateGrossBookingFee_CalculatesCorrectBalance()
    {
        var apptId = Guid.NewGuid();
        var payId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        long actualPaid = 500_000;
        long platformFee = 50_000;
        long trainerShare = 450_000;

        var grossEntry = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.GrossBookingFee,
            actualPaid,
            now,
            "system",
            appointmentId: apptId,
            paymentId: payId,
            sourceEventId: $"appt-{apptId}-completed");

        var platformEntry = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.PlatformCommission,
            platformFee,
            now,
            "system",
            appointmentId: apptId,
            paymentId: payId,
            sourceEventId: $"appt-{apptId}-completed");

        var trainerEntry = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.TrainerPayable,
            trainerShare,
            now,
            "system",
            appointmentId: apptId,
            paymentId: payId,
            sourceEventId: $"appt-{apptId}-completed");

        grossEntry.AmountVnd.Should().Be(actualPaid);
        platformEntry.AmountVnd.Should().Be(platformFee);
        trainerEntry.AmountVnd.Should().Be(trainerShare);

        // Đảm bảo phương trình cân bằng kế toán: Gross = PlatformCommission + TrainerPayable
        (platformEntry.AmountVnd + trainerEntry.AmountVnd).Should().Be(grossEntry.AmountVnd);
    }

    [Fact]
    public void FinancialLedgerEntry_CreateRefundAdjustments_CalculatesNegativeSignedAmounts()
    {
        var apptId = Guid.NewGuid();
        var payId = Guid.NewGuid();
        var refundId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        long refundAmount = 200_000;
        long platformFeeReturn = 20_000;
        long trainerShareDeduction = 180_000;

        var grossReduction = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.RefundGrossReduction,
            -refundAmount,
            now,
            "admin-1",
            appointmentId: apptId,
            paymentId: payId,
            refundId: refundId,
            sourceEventId: $"refund-{refundId}-succeeded");

        var platformReturn = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.RefundPlatformFeeReturn,
            -platformFeeReturn,
            now,
            "admin-1",
            appointmentId: apptId,
            paymentId: payId,
            refundId: refundId,
            sourceEventId: $"refund-{refundId}-succeeded");

        var trainerDeduction = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.RefundTrainerDeduction,
            -trainerShareDeduction,
            now,
            "admin-1",
            appointmentId: apptId,
            paymentId: payId,
            refundId: refundId,
            sourceEventId: $"refund-{refundId}-succeeded");

        grossReduction.AmountVnd.Should().Be(-200_000);
        platformReturn.AmountVnd.Should().Be(-20_000);
        trainerDeduction.AmountVnd.Should().Be(-180_000);

        // Đảm bảo phương trình cân bằng điều chỉnh âm: Reduction = Return + Deduction
        (platformReturn.AmountVnd + trainerDeduction.AmountVnd).Should().Be(grossReduction.AmountVnd);
    }

    [Fact]
    public void FinancialLedgerEntry_CreateTrainerPayout_CalculatesNegativePayoutAmount()
    {
        var apptId = Guid.NewGuid();
        var payId = Guid.NewGuid();
        var settlementId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        long payoutAmount = 450_000;

        var payout = FinancialLedgerEntry.Create(
            FinancialLedgerEntryTypes.TrainerPayout,
            -payoutAmount,
            now,
            "admin-finance",
            appointmentId: apptId,
            paymentId: payId,
            settlementId: settlementId,
            sourceEventId: $"settlement-{settlementId}-paid");

        payout.AmountVnd.Should().Be(-450_000);
        payout.SettlementId.Should().Be(settlementId);
        payout.EntryType.Should().Be(FinancialLedgerEntryTypes.TrainerPayout);
    }

    [Fact]
    public async Task AdminFinanceService_ListLedgerAsync_ParsesNewFinancialLedgerEntryTypes()
    {
        var storeMock = new Mock<IAdminFinanceStore>();
        var authzMock = new Mock<IAuthorizationGuard>();
        var outboxMock = new Mock<Fitmate.Application.Outbox.IOutboxDispatcher>();
        var now = DateTime.UtcNow;

        var rows = new List<FinanceLedgerRow>
        {
            new(Guid.NewGuid(), FinancialLedgerEntryTypes.PlatformCommission, 50_000, Guid.NewGuid(), null, now),
            new(Guid.NewGuid(), FinancialLedgerEntryTypes.TrainerPayable, 450_000, Guid.NewGuid(), null, now.AddHours(-1)),
            new(Guid.NewGuid(), FinancialLedgerEntryTypes.RefundGrossReduction, 100_000, Guid.NewGuid(), null, now.AddHours(-2)),
            new(Guid.NewGuid(), FinancialLedgerEntryTypes.TrainerPayout, 350_000, Guid.NewGuid(), null, now.AddHours(-3))
        };

        storeMock.Setup(s => s.ListLedgerAsync(It.IsAny<LedgerEntryType?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        storeMock.Setup(s => s.CountLedgerAsync(It.IsAny<LedgerEntryType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows.Count);

        var service = new AdminFinanceService(storeMock.Object, authzMock.Object, outboxMock.Object);

        var result = await service.GetLedgerAsync(Guid.NewGuid(), null, 1, 10, CancellationToken.None);

        result.Items.Should().HaveCount(4);
        result.Items[0].Type.Should().Be(LedgerEntryType.PlatformFee);
        result.Items[1].Type.Should().Be(LedgerEntryType.TrainerShare);
        result.Items[2].Type.Should().Be(LedgerEntryType.Refund);
        result.Items[3].Type.Should().Be(LedgerEntryType.Payout);
    }
}
