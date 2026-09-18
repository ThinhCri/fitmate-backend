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
