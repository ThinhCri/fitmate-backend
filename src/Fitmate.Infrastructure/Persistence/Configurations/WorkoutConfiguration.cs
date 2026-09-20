using Fitmate.Domain.Workout;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public class WorkoutPlanConfiguration : IEntityTypeConfiguration<WorkoutPlan>
{
    public void Configure(EntityTypeBuilder<WorkoutPlan> e)
    {
        e.ToTable("workout_plans");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.DayOfWeek).HasMaxLength(16).IsRequired();
        e.Property(x => x.WorkoutName).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.UserId, x.DayOfWeek });
    }
}

public class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> e)
    {
        e.ToTable("exercises");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.WorkoutPlanId).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Type).HasConversion<int>();
        e.Property(x => x.Tags).HasColumnType("text[]");
        e.HasIndex(x => x.WorkoutPlanId);
    }
}

public class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    public void Configure(EntityTypeBuilder<WorkoutSession> e)
    {
        e.ToTable("workout_sessions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.WorkoutPlanId);
        e.Property(x => x.ClientId).HasMaxLength(64).IsRequired();
        e.HasIndex(x => new { x.UserId, x.ClientId }).IsUnique();
        e.Property(x => x.Day).HasMaxLength(16);
        e.Property(x => x.Status).HasConversion<int>();
        e.Property(x => x.SnapshotPlanName).HasMaxLength(200);
        e.Property(x => x.SnapshotPlanDayOfWeek).HasMaxLength(16);
    }
}

public class WeightLogConfiguration : IEntityTypeConfiguration<WeightLog>
{
    public void Configure(EntityTypeBuilder<WeightLog> e)
    {
        e.ToTable("weight_logs");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).IsRequired();
        e.Property(x => x.WeightKg).HasColumnType("numeric(5,2)");
        e.Property(x => x.RecordedAt).IsRequired();
        e.HasIndex(x => new { x.UserId, x.RecordedAt });
    }
}

public class WorkoutSessionExerciseConfiguration : IEntityTypeConfiguration<WorkoutSessionExercise>
{
    public void Configure(EntityTypeBuilder<WorkoutSessionExercise> e)
    {
        e.ToTable("workout_session_exercises");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.WorkoutSessionId).IsRequired();
        e.Property(x => x.ExerciseName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Type).HasConversion<int>();
        e.Property(x => x.Tags).HasColumnType("text[]");
        e.HasIndex(x => x.WorkoutSessionId);
        e.HasMany(x => x.SetResults)
            .WithOne()
            .HasForeignKey(s => s.WorkoutSessionExerciseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WorkoutSessionExerciseSetConfiguration : IEntityTypeConfiguration<WorkoutSessionExerciseSet>
{
    public void Configure(EntityTypeBuilder<WorkoutSessionExerciseSet> e)
    {
        e.ToTable("workout_session_exercise_sets");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.WorkoutSessionExerciseId).IsRequired();
        e.Property(x => x.Status).HasConversion<int>();
        e.HasIndex(x => new { x.WorkoutSessionExerciseId, x.SetIndex }).IsUnique();
    }
}
