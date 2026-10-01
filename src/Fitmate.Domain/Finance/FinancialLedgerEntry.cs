using System;
using System.Collections.Generic;
using Fitmate.Domain.Common;

namespace Fitmate.Domain.Finance;

public static class FinancialLedgerEntryTypes
{
    public const string GrossBookingFee = "GrossBookingFee";
    public const string PlatformCommission = "PlatformCommission";
    public const string TrainerPayable = "TrainerPayable";

    public const string RefundGrossReduction = "RefundGrossReduction";
    public const string RefundPlatformFeeReturn = "RefundPlatformFeeReturn";
    public const string RefundTrainerDeduction = "RefundTrainerDeduction";

    public const string TrainerPayout = "TrainerPayout";
}

public class FinancialLedgerEntry
{
    private static readonly HashSet<string> ValidEntryTypes = new(StringComparer.Ordinal)
    {
        FinancialLedgerEntryTypes.GrossBookingFee,
        FinancialLedgerEntryTypes.PlatformCommission,
        FinancialLedgerEntryTypes.TrainerPayable,
        FinancialLedgerEntryTypes.RefundGrossReduction,
        FinancialLedgerEntryTypes.RefundPlatformFeeReturn,
        FinancialLedgerEntryTypes.RefundTrainerDeduction,
        FinancialLedgerEntryTypes.TrainerPayout
    };

    public Guid Id { get; private set; }
    public string EntryType { get; private set; } = string.Empty;
    public long AmountVnd { get; private set; } // Signed amount
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? RefundId { get; private set; }
    public Guid? SettlementId { get; private set; }
    public string? SourceEventId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string CreatedByUserId { get; private set; } = "system";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? ReversesEntryId { get; private set; }
    public Guid? CorrectsEntryId { get; private set; }
    public int MetadataVersion { get; private set; } = 1;
    public string MetadataJson { get; private set; } = "{}";

    private FinancialLedgerEntry() { } // EF Core

    public static FinancialLedgerEntry Create(
        string entryType,
        long amountVnd,
        DateTime occurredAtUtc,
        string createdByUserId,
        Guid? appointmentId = null,
        Guid? paymentId = null,
        Guid? refundId = null,
        Guid? settlementId = null,
        string? sourceEventId = null,
        string? correlationId = null,
        Guid? reversesEntryId = null,
        Guid? correctsEntryId = null,
        string metadataJson = "{}")
    {
        if (string.IsNullOrWhiteSpace(entryType) || !ValidEntryTypes.Contains(entryType))
            throw new DomainException("INVALID_LEDGER_ENTRY_TYPE", $"Invalid financial ledger entry type: '{entryType}'.");

        if (amountVnd == 0)
            throw new DomainException("INVALID_LEDGER_AMOUNT", "Financial ledger entry amount cannot be zero.");

        if (reversesEntryId.HasValue && correctsEntryId.HasValue)
            throw new DomainException("INVALID_LEDGER_RELATION", "A ledger entry cannot simultaneously be a reversal and a correction.");

        return new FinancialLedgerEntry
        {
            Id = Guid.NewGuid(),
            EntryType = entryType,
            AmountVnd = amountVnd,
            OccurredAtUtc = occurredAtUtc,
            CreatedByUserId = createdByUserId,
            AppointmentId = appointmentId,
            PaymentId = paymentId,
            RefundId = refundId,
            SettlementId = settlementId,
            SourceEventId = sourceEventId,
            CorrelationId = correlationId,
            ReversesEntryId = reversesEntryId,
            CorrectsEntryId = correctsEntryId,
            CreatedAtUtc = DateTime.UtcNow,
            MetadataVersion = 1,
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? "{}" : metadataJson
        };
    }
}
