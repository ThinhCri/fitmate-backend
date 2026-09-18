using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

public class WorkoutSession : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? WorkoutPlanId { get; private set; }
    /// <summary>
    /// Client-generated UUID chống duplicate khi retry.
    /// Unique per user.
    /// </summary>
    public string ClientId { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public string Day { get; private set; } = null!;
    public int DurationMin { get; private set; }
    public int CaloriesBurned { get; private set; }
    public int Exercises { get; private set; }
    public WorkoutSessionStatus Status { get; private set; }
    public int? CompletedSets { get; private set; }
    public int? PlannedSets { get; private set; }

    private WorkoutSession() { }
    public static WorkoutSession Create(Guid userId, Guid? planId, string clientId,
        DateOnly date, string day, int durationMin, int caloriesBurned, int exercises,
        WorkoutSessionStatus status, int? completedSets, int? plannedSets) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        WorkoutPlanId = planId,
        ClientId = clientId,
        Date = date,
        Day = day,
        DurationMin = durationMin,
        CaloriesBurned = caloriesBurned,
        Exercises = exercises,
        Status = status,
        CompletedSets = completedSets,
        PlannedSets = plannedSets,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
