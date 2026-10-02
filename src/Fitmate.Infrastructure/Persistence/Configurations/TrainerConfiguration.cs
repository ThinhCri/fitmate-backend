using Fitmate.Domain.Trainers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class PtProfileConfiguration : IEntityTypeConfiguration<PtProfile>
{
    public void Configure(EntityTypeBuilder<PtProfile> e)
    {
        e.ToTable("pt_profiles");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.HasIndex(x => x.UserId).IsUnique();
        e.Property(x => x.Specialty).HasMaxLength(200);
        e.Property(x => x.PricePerSessionVnd);
        e.Property(x => x.Rating).HasColumnType("numeric(3,2)");
        e.Property(x => x.VerificationStatus).HasConversion<int>().IsRequired();
        e.Property(x => x.VerificationReason).HasMaxLength(500);
        e.Property(x => x.VerificationReviewedAt);
        e.Property(x => x.VerificationReviewedByAdminId);
    }
}

public class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> e)
    {
        e.ToTable("certificates");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.IssuedBy).HasMaxLength(200).IsRequired();
        e.Property(x => x.Code).HasMaxLength(100).IsRequired();
        e.Property(x => x.IssueDate).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.ImageStorageKey).HasMaxLength(500);
        e.Property(x => x.RejectReason).HasMaxLength(500);
        e.HasIndex(x => new { x.PtUserId, x.Status });
    }
}

public class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> e)
    {
        e.ToTable("services");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.PtUserId, x.IsActive });
        e.Property(x => x.PriceVnd).IsRequired();
        e.Property(x => x.DurationMinutes).IsRequired();
    }
}

public class AvailabilitySlotConfiguration : IEntityTypeConfiguration<AvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<AvailabilitySlot> e)
    {
        e.ToTable("availability_slots");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.PtUserId).IsRequired();
        e.Property(x => x.SlotStartUtc).IsRequired();
        e.Property(x => x.SlotStartLocal)
            .IsRequired()
            .HasConversion(
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
                v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified));
        e.Property(x => x.DurationMinutes).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.HasIndex(x => new { x.PtUserId, x.SlotStartUtc });
    }
}
