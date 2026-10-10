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
        e.HasMany(x => x.StopRequests).WithOne().HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionPackageStopRequestConfiguration : IEntityTypeConfiguration<SessionPackageStopRequest>
{
    public void Configure(EntityTypeBuilder<SessionPackageStopRequest> e)
    {
        e.ToTable("session_package_stop_requests", t =>
        {
            t.HasCheckConstraint("chk_package_stop_reason", "length(\"Reason\") BETWEEN 10 AND 1000");
            t.HasCheckConstraint("chk_package_stop_quote", "\"RefundQuoteVnd\" >= 0");
        });
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.PackageId).IsUnique().HasFilter("\"Status\" IN (0, 1)");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.ApprovalSource).HasConversion<int>();
        e.HasMany(x => x.Sessions).WithOne().HasForeignKey(x => x.StopRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionPackageStopSessionConfiguration : IEntityTypeConfiguration<SessionPackageStopSession>
{
    public void Configure(EntityTypeBuilder<SessionPackageStopSession> e)
    {
        e.ToTable("session_package_stop_sessions", t =>
        {
            t.HasCheckConstraint("chk_package_stop_session_money", "\"PaidAllocationVnd\" > 0");
        });
        e.HasKey(x => x.Id);
        e.HasIndex(x => new { x.StopRequestId, x.AppointmentId }).IsUnique();
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.StatusAtRequest).HasConversion<int>();
        e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
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
        e.HasIndex(x => x.HoldId).IsUnique();
        e.HasIndex(x => x.AppointmentId).IsUnique();
        e.HasOne<BookingHold>().WithMany().HasForeignKey(x => x.HoldId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<Appointment>().WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
