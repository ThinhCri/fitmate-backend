using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;

namespace Fitmate.Application.Workout;

// ─── Query ─────────────────────────────────────────────────────────────────

public sealed record WorkoutPlanView(
    Guid Id,
    string DayOfWeek,
    string WorkoutName,
    int DurationMinutes,
    int TargetCaloriesBurn,
    bool IsCompleted,
    int ExerciseCount,
    DateTime CreatedAt);

public sealed record WorkoutPlanDetailView(
    Guid Id,
    string DayOfWeek,
    string WorkoutName,
    int DurationMinutes,
    int TargetCaloriesBurn,
    bool IsCompleted,
    DateTime CreatedAt,
    IReadOnlyList<ExerciseItemView> Exercises);

public sealed record ExerciseItemView(
    Guid Id,
    Guid WorkoutPlanId,
    string Name,
    ExerciseType Type,
    int Sets,
    int? Reps,
    int? DurationSec,
    int RestSec,
    int? RestAfterSec,
    int Calories,
    string? Notes,
    string? MediaUrl,
    string? MediaType,
    string? MediaSource,
    List<string> Tags);

public sealed record WorkoutSessionView(
    Guid Id,
    Guid? PlanId,
    string? PlanName,
    DateOnly Date,
    string Day,
    int DurationMin,
    int CaloriesBurned,
    int Exercises,
    WorkoutSessionStatus Status,
    int? CompletedSets,
    int? PlannedSets,
    DateTime CreatedAt);

public sealed record WorkoutSessionDetailView(
    Guid Id,
    Guid? PlanId,
    string? PlanName,
    DateOnly Date,
    string Day,
    int DurationMin,
    int CaloriesBurned,
    int Exercises,
    WorkoutSessionStatus Status,
    int? CompletedSets,
    int? PlannedSets,
    DateTime CreatedAt,
    IReadOnlyList<WorkoutSessionExerciseView> Items);

public sealed record WorkoutSessionExerciseView(
    Guid Id,
    string ExerciseName,
    ExerciseType Type,
    int OrderIndex,
    int PlannedSets,
    int? PlannedReps,
    int? PlannedDurationSec,
    int? PlannedRestSec,
    int PlannedCalories,
    List<string> Tags,
    int CompletedSets,
    IReadOnlyList<WorkoutSetView> SetResults);

public sealed record WorkoutSetView(
    Guid Id,
    int SetIndex,
    WorkoutSetStatus Status,
    int? ActualReps,
    int? ActualDurationSec,
    bool IsBonus);

public sealed record WeightLogView(
    Guid Id,
    decimal WeightKg,
    DateTime RecordedAt);

// ─── Command ─────────────────────────────────────────────────────────────────

public sealed record LogWorkoutSessionRequest
{
    public required string ClientId { get; init; }
    public Guid? PlanId { get; init; }
    public required DateOnly Date { get; init; }
    public required int DurationMin { get; init; }
    public required int CaloriesBurned { get; init; }
    public required int Exercises { get; init; }
    public required WorkoutSessionStatus Status { get; init; }
    public int? CompletedSets { get; init; }
    public int? PlannedSets { get; init; }
    public List<LogExerciseRequest>? ExerciseItems { get; init; }
}

public sealed record LogExerciseRequest
{
    public required Guid ExerciseId { get; init; }
    public required int OrderIndex { get; init; }
    public int? PlannedReps { get; init; }
    public int? PlannedDurationSec { get; init; }
    public int? PlannedRestSec { get; init; }
    public List<LogSetRequest>? Sets { get; init; }
}

public sealed record LogSetRequest
{
    public required int SetIndex { get; init; }
    public required WorkoutSetStatus Status { get; init; }
    public int? ActualReps { get; init; }
    public int? ActualDurationSec { get; init; }
}

public sealed record LogWeightRequest
{
    public required decimal WeightKg { get; init; }
    public DateTime? RecordedAt { get; init; }
}

public sealed record CreateWorkoutPlanRequest
{
    public required string DayOfWeek { get; init; }
    public required string WorkoutName { get; init; }
    public required int DurationMinutes { get; init; }
    public required int TargetCaloriesBurn { get; init; }
    public List<WorkoutPlanExerciseInput>? Exercises { get; init; }
}

public sealed record UpdateWorkoutPlanRequest
{
    public string? DayOfWeek { get; init; }
    public string? WorkoutName { get; init; }
    public int? DurationMinutes { get; init; }
    public int? TargetCaloriesBurn { get; init; }
    public bool? IsCompleted { get; init; }
    public List<WorkoutPlanExerciseInput>? Exercises { get; init; }
}

public sealed record WorkoutPlanExerciseInput
{
    public required string Name { get; init; }
    public required ExerciseType Type { get; init; }
    public required int Sets { get; init; }
    public int? Reps { get; init; }
    public int? DurationSec { get; init; }
    public int RestSec { get; init; }
    public int? RestAfterSec { get; init; }
    public int Calories { get; init; }
    public string? Notes { get; init; }
    public string? MediaUrl { get; init; }
    public string? MediaType { get; init; }
    public string? MediaSource { get; init; }
    public List<string>? Tags { get; init; }
}
