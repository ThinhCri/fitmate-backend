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
