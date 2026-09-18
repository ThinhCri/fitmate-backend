using Fitmate.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> e)
    {
        e.ToTable("reports");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ReporterId).IsRequired();
        e.Property(x => x.TargetType).HasConversion<int>();
        e.Property(x => x.TargetId).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(64).IsRequired();
        e.Property(x => x.Details).HasMaxLength(2000);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.AdminNote).HasMaxLength(1000);
        e.HasIndex(x => new { x.Status, x.CreatedAt });
        e.HasIndex(x => new { x.TargetType, x.TargetId });
    }
}
