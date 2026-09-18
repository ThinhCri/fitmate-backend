using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

public class WorkoutPlan : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string DayOfWeek { get; private set; } = null!;
    public string WorkoutName { get; private set; } = null!;
    public int DurationMinutes { get; private set; }
    public int TargetCaloriesBurn { get; private set; }
    public bool IsCompleted { get; private set; }

    private WorkoutPlan() { }
    public static WorkoutPlan Create(Guid userId, string dayOfWeek, string workoutName, int durationMinutes, int targetCaloriesBurn) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        DayOfWeek = dayOfWeek,
        WorkoutName = workoutName,
        DurationMinutes = durationMinutes,
        TargetCaloriesBurn = targetCaloriesBurn,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void MarkCompleted() { IsCompleted = true; Touch(); }
}
