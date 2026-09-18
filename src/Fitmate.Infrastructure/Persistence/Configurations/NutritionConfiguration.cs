using Fitmate.Domain.Nutrition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class MealLogConfiguration : IEntityTypeConfiguration<MealLog>
{
    public void Configure(EntityTypeBuilder<MealLog> e)
    {
        e.ToTable("meal_logs");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.FoodName).HasMaxLength(200).IsRequired();
        e.Property(x => x.ImageStorageKey).HasMaxLength(500);
        e.Property(x => x.Source).HasConversion<int>();
        e.Property(x => x.AiConfidence).HasColumnType("numeric(3,2)");
        e.HasIndex(x => new { x.UserId, x.LoggedAt });
    }
}

public class MealScheduleConfiguration : IEntityTypeConfiguration<MealSchedule>
{
    public void Configure(EntityTypeBuilder<MealSchedule> e)
    {
        e.ToTable("meal_schedules");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.HasIndex(x => x.UserId).IsUnique();
    }
}
