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

    // BE-160: snapshot tối thiểu của plan tại thời điểm insert session, để sửa/xóa plan
    // không phá lịch sử. Source of truth vẫn là workout_plan_exercises / exercises cho
    // plan hiện tại; snapshot là dữ liệu "đã từng tập".
    public string? SnapshotPlanName { get; private set; }
    public string? SnapshotPlanDayOfWeek { get; private set; }
    public int? SnapshotPlanPlannedDurationMinutes { get; private set; }
    public int? SnapshotPlanTargetCaloriesBurn { get; private set; }

    private WorkoutSession() { }
    public static WorkoutSession Create(Guid userId, Guid? planId, string clientId,
        DateOnly date, string day, int durationMin, int caloriesBurned, int exercises,
        WorkoutSessionStatus status, int? completedSets, int? plannedSets,
        string? snapshotPlanName = null,
        string? snapshotPlanDayOfWeek = null,
        int? snapshotPlanPlannedDurationMinutes = null,
        int? snapshotPlanTargetCaloriesBurn = null) => new()
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
        SnapshotPlanName = snapshotPlanName,
        SnapshotPlanDayOfWeek = snapshotPlanDayOfWeek,
        SnapshotPlanPlannedDurationMinutes = snapshotPlanPlannedDurationMinutes,
        SnapshotPlanTargetCaloriesBurn = snapshotPlanTargetCaloriesBurn,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
