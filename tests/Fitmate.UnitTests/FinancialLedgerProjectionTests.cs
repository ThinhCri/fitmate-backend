using System;
using System.Collections.Generic;
using System.Linq;
using Fitmate.Application.Finance;
using Fitmate.Domain.Finance;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

public class FinancialLedgerProjectionTests
{
    [Fact]
    public void LedgerDimensionBalances_CompletedBooking_SeparatesDimensionsWithoutDoubleCounting()
    {
        // Given a completed booking that generated 3 entries:
        var entries = new List<FinancialLedgerEntry>
        {
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, 400_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, 40_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, 360_000, DateTime.UtcNow, "system")
        };

        // Old buggy behavior: Sum of everything was 800,000 (double-counting gross + fee + payable)
        var oldNaiveSum = entries.Sum(e => e.AmountVnd);
        oldNaiveSum.Should().Be(800_000);

        // New isolated dimension calculation:
        var map = entries.GroupBy(e => e.EntryType).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountVnd));
        long gross = map.GetValueOrDefault(FinancialLedgerEntryTypes.GrossBookingFee) +
                     map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundGrossReduction);
        long fee = map.GetValueOrDefault(FinancialLedgerEntryTypes.PlatformCommission) +
                   map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundPlatformFeeReturn);
        long payable = map.GetValueOrDefault(FinancialLedgerEntryTypes.TrainerPayable) +
                       map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundTrainerDeduction) +
                       map.GetValueOrDefault(FinancialLedgerEntryTypes.TrainerPayout);

        var balances = new LedgerDimensionBalances(gross, fee, payable);

        balances.GrossBalanceVnd.Should().Be(400_000);
        balances.PlatformFeeBalanceVnd.Should().Be(40_000);
        balances.TrainerPayableBalanceVnd.Should().Be(360_000);
    }

    [Fact]
    public void LedgerDimensionBalances_FullRefund_OffsetsAllBalancesToZero()
    {
        var entries = new List<FinancialLedgerEntry>
        {
            // Initial recognition
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, 500_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, 50_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, 450_000, DateTime.UtcNow, "system"),
            // Full refund
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundGrossReduction, -500_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundPlatformFeeReturn, -50_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.RefundTrainerDeduction, -450_000, DateTime.UtcNow, "system")
        };

        var map = entries.GroupBy(e => e.EntryType).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountVnd));
        long gross = map.GetValueOrDefault(FinancialLedgerEntryTypes.GrossBookingFee) +
                     map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundGrossReduction);
        long fee = map.GetValueOrDefault(FinancialLedgerEntryTypes.PlatformCommission) +
                   map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundPlatformFeeReturn);
        long payable = map.GetValueOrDefault(FinancialLedgerEntryTypes.TrainerPayable) +
                       map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundTrainerDeduction) +
                       map.GetValueOrDefault(FinancialLedgerEntryTypes.TrainerPayout);

        var balances = new LedgerDimensionBalances(gross, fee, payable);

        balances.GrossBalanceVnd.Should().Be(0);
        balances.PlatformFeeBalanceVnd.Should().Be(0);
        balances.TrainerPayableBalanceVnd.Should().Be(0);
    }

    [Fact]
    public void CumulativeRefund_MultiStepRounding_MaintainsZeroDiscrepancy()
    {
        long actualPaidVnd = 100_000;
        long totalPlatformFeeVnd = 10_000;
        long totalTrainerShareVnd = 90_000;

        // Step 1: Refund 33,333
        long r1 = 33_333;
        long cumR1 = r1;
        long cumFee1 = (long)Math.Round(totalPlatformFeeVnd * (decimal)cumR1 / actualPaidVnd, MidpointRounding.AwayFromZero);
        long deltaFee1 = cumFee1;
        long deltaTrainer1 = r1 - deltaFee1;

        // Step 2: Refund 33,333
        long r2 = 33_333;
        long cumR2 = cumR1 + r2; // 66,666
        long cumFee2 = (long)Math.Round(totalPlatformFeeVnd * (decimal)cumR2 / actualPaidVnd, MidpointRounding.AwayFromZero);
        long deltaFee2 = cumFee2 - cumFee1;
        long deltaTrainer2 = r2 - deltaFee2;

        // Step 3: Refund remaining 33,334
        long r3 = 33_334;
        long cumR3 = cumR2 + r3; // 100,000
        long cumFee3 = (long)Math.Round(totalPlatformFeeVnd * (decimal)cumR3 / actualPaidVnd, MidpointRounding.AwayFromZero);
        long deltaFee3 = cumFee3 - cumFee2;
        long deltaTrainer3 = r3 - deltaFee3;

        // Total fee deductions must exactly equal total platform fee
        (deltaFee1 + deltaFee2 + deltaFee3).Should().Be(totalPlatformFeeVnd);

        // Total trainer deductions must exactly equal total trainer share
        (deltaTrainer1 + deltaTrainer2 + deltaTrainer3).Should().Be(totalTrainerShareVnd);

        // Total gross refunded must equal actual paid
        (r1 + r2 + r3).Should().Be(actualPaidVnd);
    }

    [Fact]
    public void LedgerDimensionBalances_TrainerPayout_ReducesPayableToZero()
    {
        var entries = new List<FinancialLedgerEntry>
        {
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, 200_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, 20_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, 180_000, DateTime.UtcNow, "system"),
            FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayout, -180_000, DateTime.UtcNow, "system")
        };

        var map = entries.GroupBy(e => e.EntryType).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountVnd));
        long gross = map.GetValueOrDefault(FinancialLedgerEntryTypes.GrossBookingFee) +
                     map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundGrossReduction);
        long fee = map.GetValueOrDefault(FinancialLedgerEntryTypes.PlatformCommission) +
                   map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundPlatformFeeReturn);
        long payable = map.GetValueOrDefault(FinancialLedgerEntryTypes.TrainerPayable) +
                       map.GetValueOrDefault(FinancialLedgerEntryTypes.RefundTrainerDeduction) +
                       map.GetValueOrDefault(FinancialLedgerEntryTypes.TrainerPayout);

        var balances = new LedgerDimensionBalances(gross, fee, payable);

        balances.GrossBalanceVnd.Should().Be(200_000);
        balances.PlatformFeeBalanceVnd.Should().Be(20_000);
        balances.TrainerPayableBalanceVnd.Should().Be(0);
    }
}
