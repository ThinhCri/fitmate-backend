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
        e.Property(x => x.MealSlot).HasConversion<int>();
        e.Property(x => x.ServingQuantity).HasColumnType("numeric(10,2)");
        e.Property(x => x.ServingUnit).HasMaxLength(50);
        e.Property(x => x.Grams).HasColumnType("numeric(10,2)");
        e.Property(x => x.Notes).HasMaxLength(1000);
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

public sealed class WeeklyMealPlanItemConfiguration : IEntityTypeConfiguration<WeeklyMealPlanItem>
{
    public void Configure(EntityTypeBuilder<WeeklyMealPlanItem> e)
    {
        e.ToTable("weekly_meal_plan_items");
        e.HasKey(x => x.Id);
        e.Property(x => x.FoodName).HasMaxLength(200).IsRequired();
        e.Property(x => x.WeekStart).HasColumnType("date");
        e.Property(x => x.MealSlot).HasConversion<int>();
        e.Property(x => x.ServingQuantity).HasColumnType("numeric(10,2)");
        e.Property(x => x.ServingUnit).HasMaxLength(50);
        e.Property(x => x.Grams).HasColumnType("numeric(10,2)");
        e.Property(x => x.Notes).HasMaxLength(1000);
        e.HasIndex(x => new { x.UserId, x.WeekStart, x.DayOfWeek, x.MealSlot });
    }
}

public sealed class WeeklyMealPlanConsumptionConfiguration : IEntityTypeConfiguration<WeeklyMealPlanConsumption>
{
    public void Configure(EntityTypeBuilder<WeeklyMealPlanConsumption> e)
    {
        e.ToTable("weekly_meal_plan_consumptions");
        e.HasKey(x => x.Id);
        e.Property(x => x.ConsumedDate).HasColumnType("date");
        e.HasIndex(x => new { x.PlanItemId, x.ConsumedDate }).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ConsumedDate });
        e.HasOne<WeeklyMealPlanItem>()
            .WithMany()
            .HasForeignKey(x => x.PlanItemId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne<MealLog>()
            .WithMany()
            .HasForeignKey(x => x.MealLogId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
