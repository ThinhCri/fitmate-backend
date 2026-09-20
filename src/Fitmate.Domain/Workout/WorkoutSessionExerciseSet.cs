using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

/// <summary>
/// BE-160: One row per set result inside a session exercise. UNIQUE
/// (parent, set_index) at DB level enforces single entry per index per exercise.
/// Bonus flag is derived from <c>SetIndex &gt; parent.PlannedSets</c> at insert time.
/// </summary>
public class WorkoutSessionExerciseSet : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid WorkoutSessionExerciseId { get; private set; }

    public int SetIndex { get; private set; }
    public WorkoutSetStatus Status { get; private set; }

    /// <summary>Used for <see cref="ExerciseType.Reps"/>. Null when type=time or unknown.</summary>
    public int? ActualReps { get; private set; }

    /// <summary>Used for <see cref="ExerciseType.Time"/>. Null when type=reps or unknown.</summary>
    public int? ActualDurationSec { get; private set; }

    public bool IsBonus { get; private set; }

    private WorkoutSessionExerciseSet() { }

    public static WorkoutSessionExerciseSet Create(
        Guid workoutSessionExerciseId,
        int setIndex,
        WorkoutSetStatus status,
        int? actualReps,
        int? actualDurationSec,
        bool isBonus) => new()
    {
        Id = Guid.NewGuid(),
        WorkoutSessionExerciseId = workoutSessionExerciseId,
        SetIndex = setIndex,
        Status = status,
        ActualReps = actualReps,
        ActualDurationSec = actualDurationSec,
        IsBonus = isBonus,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
