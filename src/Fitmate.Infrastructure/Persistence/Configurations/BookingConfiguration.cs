using Fitmate.Domain.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> e)
    {
        e.ToTable("appointments");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MemberId).IsRequired();
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.ServiceId).IsRequired();
        e.Property(x => x.SlotStartUtc).IsRequired();
        e.Property(x => x.SlotStartLocal)
            .IsRequired()
            .HasConversion(
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));
        e.Property(x => x.DurationMinutes).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.PlatformFeeRate).HasColumnType("numeric(5,4)");
        e.HasIndex(x => new { x.PtUserId, x.SlotStartUtc });
        e.HasIndex(x => new { x.MemberId, x.SlotStartUtc });
        e.HasIndex(x => x.PaymentId);
    }
}

public class BookingHoldConfiguration : IEntityTypeConfiguration<BookingHold>
{
    public void Configure(EntityTypeBuilder<BookingHold> e)
    {
        e.ToTable("booking_holds");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MemberId).IsRequired();
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.ServiceId).IsRequired();
        e.Property(x => x.SlotStartUtc).IsRequired();
        e.Property(x => x.ExpiresAt).IsRequired();
        e.Property(x => x.Status).HasMaxLength(16).IsRequired();
        e.HasIndex(x => x.ExpiresAt);
    }
}
