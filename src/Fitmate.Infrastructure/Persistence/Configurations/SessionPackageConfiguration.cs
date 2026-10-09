using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public sealed class SessionPackageConfiguration : IEntityTypeConfiguration<SessionPackage>
{
    public void Configure(EntityTypeBuilder<SessionPackage> e)
    {
        e.ToTable("session_packages", t =>
        {
            t.HasCheckConstraint("chk_session_package_count", "\"SessionCount\" IN (4, 8)");
            t.HasCheckConstraint("chk_session_package_price", "\"TotalPriceVnd\" > 0");
        });
        e.HasKey(x => x.Id);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.TotalPriceVnd).IsRequired();
        e.HasIndex(x => new { x.MemberId, x.CreatedAt });
        e.HasIndex(x => new { x.PtUserId, x.Status });
        e.HasIndex(x => x.PaymentId).IsUnique().HasFilter("\"PaymentId\" IS NOT NULL");
        e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        e.HasMany(x => x.Sessions).WithOne().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionPackageSessionConfiguration : IEntityTypeConfiguration<SessionPackageSession>
{
    public void Configure(EntityTypeBuilder<SessionPackageSession> e)
    {
        e.ToTable("session_package_sessions", t =>
        {
            t.HasCheckConstraint("chk_package_session_sequence", "\"Sequence\" BETWEEN 1 AND 8");
            t.HasCheckConstraint("chk_package_session_money", "\"PaidAllocationVnd\" > 0 AND \"PlatformFeeAllocationVnd\" >= 0 AND \"TrainerShareAllocationVnd\" >= 0 AND \"PaidAllocationVnd\" = \"PlatformFeeAllocationVnd\" + \"TrainerShareAllocationVnd\"");
        });
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.PackageId, x.Sequence }).IsUnique();
        e.HasIndex(x => x.AppointmentId).IsUnique();
        e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
