using Fitmate.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public sealed class AiUsageConfiguration : IEntityTypeConfiguration<AiUsageReservationRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageReservationRecord> e)
    {
        e.ToTable("ai_usage_reservations");
        e.HasKey(x => x.Id);
        e.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        e.Property(x => x.State).HasMaxLength(16).IsRequired();
        e.HasIndex(x => new { x.UserId, x.LocalDate, x.Kind, x.State });
    }
}
