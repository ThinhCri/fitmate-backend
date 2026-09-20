using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

/// <summary>
/// BE-160: Per-exercise record inside a <see cref="WorkoutSession"/>. Stores the
/// planned snapshot (copied from <see cref="Exercise"/> at log time) and exposes
/// a <see cref="CompletedSets"/> count derived from <see cref="SetResults"/>.
/// The DB stores the derived count for query convenience; the canonical
/// source of truth is the set rows (UNIQUE on (parent, set_index)).
/// </summary>
public class WorkoutSessionExercise : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid WorkoutSessionId { get; private set; }

    public string ExerciseName { get; private set; } = null!;
    public ExerciseType Type { get; private set; }
    public int OrderIndex { get; private set; }

    public int PlannedSets { get; private set; }
    public int? PlannedReps { get; private set; }
    public int? PlannedDurationSec { get; private set; }
    public int? PlannedRestSec { get; private set; }
    public int PlannedCalories { get; private set; }

    public List<string> Tags { get; private set; } = new();

    public int CompletedSets { get; private set; }

    private readonly List<WorkoutSessionExerciseSet> _setResults = new();
    public IReadOnlyList<WorkoutSessionExerciseSet> SetResults => _setResults;

    private WorkoutSessionExercise() { }

    public static WorkoutSessionExercise Create(
        Guid workoutSessionId,
        string exerciseName,
        ExerciseType type,
        int orderIndex,
        int plannedSets,
        int? plannedReps,
        int? plannedDurationSec,
        int? plannedRestSec,
        int plannedCalories,
        List<string> tags) => new()
    {
        Id = Guid.NewGuid(),
        WorkoutSessionId = workoutSessionId,
        ExerciseName = exerciseName,
        Type = type,
        OrderIndex = orderIndex,
        PlannedSets = plannedSets,
        PlannedReps = plannedReps,
        PlannedDurationSec = plannedDurationSec,
        PlannedRestSec = plannedRestSec,
        PlannedCalories = plannedCalories,
        Tags = tags ?? new List<string>(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>
    /// Recompute <see cref="CompletedSets"/> from current <see cref="SetResults"/>.
    /// Counts sets whose status is <see cref="WorkoutSetStatus.Completed"/>.
    /// Bonus sets (<c>SetIndex &gt; PlannedSets</c>) are excluded by default per
    /// contract; if the contract is later extended to allow bonus, adjust here.
    /// </summary>
    public void RecomputeCompletedSets()
    {
        CompletedSets = _setResults.Count(s =>
            s.Status == WorkoutSetStatus.Completed && !s.IsBonus);
        Touch();
    }

    internal void AttachSet(WorkoutSessionExerciseSet set) => _setResults.Add(set);
}
