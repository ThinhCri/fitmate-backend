using Fitmate.Domain.Finance;
using Fitmate.Domain.Payment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.ToTable("payments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.AppointmentId).IsRequired();
        e.Property(x => x.Method).HasConversion<int>();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.AmountVnd).IsRequired();
        e.Property(x => x.RedirectUrl).HasMaxLength(1000);
        e.Property(x => x.ProviderTxnRef).HasMaxLength(128);
        e.Property(x => x.RefundedSoFarVnd).IsRequired();
        e.HasIndex(x => x.ProviderTxnRef);
        e.HasIndex(x => new { x.Method, x.ProviderTxnRef }).IsUnique()
            .HasFilter("\"Method\" = 1 AND \"ProviderTxnRef\" IS NOT NULL");
        e.HasIndex(x => x.AppointmentId);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> e)
    {
        e.ToTable("refunds");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.PaymentId).IsRequired();
        e.Property(x => x.AppointmentId).IsRequired();
        e.Property(x => x.AmountVnd).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.RequestedByAdminId).IsRequired();
        e.Property(x => x.ProviderRefundRef).HasMaxLength(128);
        e.Property(x => x.SucceededAtUtc);
        e.HasIndex(x => new { x.PaymentId, x.Status });
    }
}

public sealed class PayosOrderConfiguration : IEntityTypeConfiguration<PayosOrder>
{
    public void Configure(EntityTypeBuilder<PayosOrder> e)
    {
        e.ToTable("payos_orders");
        e.HasKey(x => x.OrderCode);
        e.Property(x => x.OrderCode).UseIdentityByDefaultColumn();
        e.HasIndex(x => x.PaymentId).IsUnique();
        e.Property(x => x.PaymentLinkId).HasMaxLength(64);
        e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ManualPtSettlementConfiguration : IEntityTypeConfiguration<ManualPtSettlement>
{
    public void Configure(EntityTypeBuilder<ManualPtSettlement> e)
    {
        e.ToTable("manual_pt_settlements");
        e.HasKey(x => x.AppointmentId);
        e.Property(x => x.AppointmentId).ValueGeneratedNever();
        e.Property(x => x.Status).HasMaxLength(20);
        e.Property(x => x.SessionEvidenceReference).HasMaxLength(200);
        e.Property(x => x.PayeeEvidenceReference).HasMaxLength(200);
        e.Property(x => x.TransferEvidenceReference).HasMaxLength(200);
        e.Property(x => x.HoldReason).HasMaxLength(200);
        e.Property(x => x.BankReference).HasMaxLength(200);
        e.HasIndex(x => x.BankReference).IsUnique();
        e.HasIndex(x => x.PtUserId);
        e.HasOne<Fitmate.Domain.Booking.Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FinancialLedgerEntryConfiguration : IEntityTypeConfiguration<FinancialLedgerEntry>
{
    public void Configure(EntityTypeBuilder<FinancialLedgerEntry> b)
    {
        b.ToTable("financial_ledger_entries", t =>
        {
            t.HasCheckConstraint("chk_ledger_entry_type", "entry_type IN ('GrossBookingFee', 'PlatformCommission', 'TrainerPayable', 'RefundGrossReduction', 'RefundPlatformFeeReturn', 'RefundTrainerDeduction', 'TrainerPayout')");
            t.HasCheckConstraint("chk_ledger_amount_nonzero", "amount_vnd != 0");
            t.HasCheckConstraint("chk_ledger_reverses_corrects_exclusive", "NOT (reverses_entry_id IS NOT NULL AND corrects_entry_id IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.EntryType).HasColumnName("entry_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.AmountVnd).HasColumnName("amount_vnd").IsRequired();
        b.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        b.Property(x => x.AppointmentId).HasColumnName("appointment_id");
        b.Property(x => x.PaymentId).HasColumnName("payment_id");
        b.Property(x => x.RefundId).HasColumnName("refund_id");
        b.Property(x => x.SettlementId).HasColumnName("settlement_id");
        b.Property(x => x.SourceEventId).HasColumnName("source_event_id").HasMaxLength(128);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        b.Property(x => x.ReversesEntryId).HasColumnName("reverses_entry_id");
        b.Property(x => x.CorrectsEntryId).HasColumnName("corrects_entry_id");
        b.Property(x => x.MetadataVersion).HasColumnName("metadata_version").IsRequired();
        b.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb").IsRequired();

        b.HasOne<Fitmate.Domain.Booking.Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Refund>()
            .WithMany()
            .HasForeignKey(x => x.RefundId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ManualPtSettlement>()
            .WithMany()
            .HasForeignKey(x => x.SettlementId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<FinancialLedgerEntry>()
            .WithMany()
            .HasForeignKey(x => x.ReversesEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<FinancialLedgerEntry>()
            .WithMany()
            .HasForeignKey(x => x.CorrectsEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AppointmentId, x.EntryType })
            .HasDatabaseName("uq_ledger_appointment_recognition")
            .IsUnique()
            .HasFilter("appointment_id IS NOT NULL AND entry_type IN ('GrossBookingFee', 'PlatformCommission', 'TrainerPayable') AND reverses_entry_id IS NULL AND corrects_entry_id IS NULL");

        b.HasIndex(x => new { x.RefundId, x.EntryType })
            .HasDatabaseName("uq_ledger_refund_adjustment")
            .IsUnique()
            .HasFilter("refund_id IS NOT NULL AND reverses_entry_id IS NULL AND corrects_entry_id IS NULL");

        b.HasIndex(x => new { x.SettlementId, x.EntryType })
            .HasDatabaseName("uq_ledger_settlement_payout")
            .IsUnique()
            .HasFilter("settlement_id IS NOT NULL AND reverses_entry_id IS NULL AND corrects_entry_id IS NULL");

        b.HasIndex(x => x.ReversesEntryId)
            .HasDatabaseName("uq_ledger_single_reversal")
            .IsUnique()
            .HasFilter("reverses_entry_id IS NOT NULL");

        b.HasIndex(x => x.CorrectsEntryId)
            .HasDatabaseName("uq_ledger_single_correction")
            .IsUnique()
            .HasFilter("corrects_entry_id IS NOT NULL");

        b.HasIndex(x => x.OccurredAtUtc);
    }
}

