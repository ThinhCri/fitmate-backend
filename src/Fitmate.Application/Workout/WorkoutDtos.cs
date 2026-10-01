using System.Text.Json;
using System.Text.Json.Serialization;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;

namespace Fitmate.Application.Workout;

public sealed class ExerciseTypeJsonConverter : JsonStringEnumConverter<ExerciseType>
{
    public ExerciseTypeJsonConverter() : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: true) { }
}

public sealed class WorkoutSessionStatusJsonConverter : JsonStringEnumConverter<WorkoutSessionStatus>
{
    public WorkoutSessionStatusJsonConverter() : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: true) { }
}

public sealed class WorkoutSetStatusJsonConverter : JsonStringEnumConverter<WorkoutSetStatus>
{
    public WorkoutSetStatusJsonConverter() : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: true) { }
}

// ─── Query ─────────────────────────────────────────────────────────────────

public sealed record WorkoutPlanView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("day_of_week")] string DayOfWeek,
    [property: JsonPropertyName("workout_name")] string WorkoutName,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("target_calories_burn")] int TargetCaloriesBurn,
    [property: JsonPropertyName("is_completed")] bool IsCompleted,
    [property: JsonPropertyName("exercise_count")] int ExerciseCount,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public sealed record WorkoutPlanDetailView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("day_of_week")] string DayOfWeek,
    [property: JsonPropertyName("workout_name")] string WorkoutName,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("target_calories_burn")] int TargetCaloriesBurn,
    [property: JsonPropertyName("is_completed")] bool IsCompleted,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("exercises")] IReadOnlyList<ExerciseItemView> Exercises);

public sealed record ExerciseItemView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("workout_plan_id")] Guid WorkoutPlanId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")]
    [property: JsonConverter(typeof(ExerciseTypeJsonConverter))] ExerciseType Type,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int? Reps,
    [property: JsonPropertyName("duration_sec")] int? DurationSec,
    [property: JsonPropertyName("rest_sec")] int RestSec,
    [property: JsonPropertyName("rest_after_sec")] int? RestAfterSec,
    [property: JsonPropertyName("calories")] int Calories,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("media_url")] string? MediaUrl,
    [property: JsonPropertyName("media_type")] string? MediaType,
    [property: JsonPropertyName("media_source")] string? MediaSource,
    [property: JsonPropertyName("tags")] List<string> Tags);

public sealed record WorkoutSessionView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("plan_id")] Guid? PlanId,
    [property: JsonPropertyName("plan_name")] string? PlanName,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("duration_min")] int DurationMin,
    [property: JsonPropertyName("calories_burned")] int CaloriesBurned,
    [property: JsonPropertyName("exercises")] int Exercises,
    [property: JsonPropertyName("status")]
    [property: JsonConverter(typeof(WorkoutSessionStatusJsonConverter))] WorkoutSessionStatus Status,
    [property: JsonPropertyName("completed_sets")] int? CompletedSets,
    [property: JsonPropertyName("planned_sets")] int? PlannedSets,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public sealed record WorkoutSessionDetailView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("plan_id")] Guid? PlanId,
    [property: JsonPropertyName("plan_name")] string? PlanName,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("duration_min")] int DurationMin,
    [property: JsonPropertyName("calories_burned")] int CaloriesBurned,
    [property: JsonPropertyName("exercises")] int Exercises,
    [property: JsonPropertyName("status")]
    [property: JsonConverter(typeof(WorkoutSessionStatusJsonConverter))] WorkoutSessionStatus Status,
    [property: JsonPropertyName("completed_sets")] int? CompletedSets,
    [property: JsonPropertyName("planned_sets")] int? PlannedSets,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("items")] IReadOnlyList<WorkoutSessionExerciseView> Items);

public sealed record WorkoutSessionExerciseView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("exercise_name")] string ExerciseName,
    [property: JsonPropertyName("type")]
    [property: JsonConverter(typeof(ExerciseTypeJsonConverter))] ExerciseType Type,
    [property: JsonPropertyName("order_index")] int OrderIndex,
    [property: JsonPropertyName("planned_sets")] int PlannedSets,
    [property: JsonPropertyName("planned_reps")] int? PlannedReps,
    [property: JsonPropertyName("planned_duration_sec")] int? PlannedDurationSec,
    [property: JsonPropertyName("planned_rest_sec")] int? PlannedRestSec,
    [property: JsonPropertyName("planned_calories")] int PlannedCalories,
    [property: JsonPropertyName("tags")] List<string> Tags,
    [property: JsonPropertyName("completed_sets")] int CompletedSets,
    [property: JsonPropertyName("set_results")] IReadOnlyList<WorkoutSetView> SetResults);

public sealed record WorkoutSetView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("set_index")] int SetIndex,
    [property: JsonPropertyName("status")]
    [property: JsonConverter(typeof(WorkoutSetStatusJsonConverter))] WorkoutSetStatus Status,
    [property: JsonPropertyName("actual_reps")] int? ActualReps,
    [property: JsonPropertyName("actual_duration_sec")] int? ActualDurationSec,
    [property: JsonPropertyName("is_bonus")] bool IsBonus);

public sealed record WeightLogView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("weight_kg")] decimal WeightKg,
    [property: JsonPropertyName("recorded_at")] DateTime RecordedAt);

public sealed record WorkoutPlanListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WorkoutPlanView> Items);

public sealed record WorkoutSessionListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WorkoutSessionView> Items);

public sealed record WeightLogListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WeightLogView> Items);

// ─── Command ─────────────────────────────────────────────────────────────────

public sealed record LogWorkoutSessionRequest
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("plan_id")]
    public Guid? PlanId { get; init; }

    [JsonPropertyName("date")]
    public required DateOnly Date { get; init; }

    [JsonPropertyName("duration_min")]
    public required int DurationMin { get; init; }

    [JsonPropertyName("calories_burned")]
    public required int CaloriesBurned { get; init; }

    [JsonPropertyName("exercises")]
    public required int Exercises { get; init; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(WorkoutSessionStatusJsonConverter))]
    public required WorkoutSessionStatus Status { get; init; }

    [JsonPropertyName("completed_sets")]
    public int? CompletedSets { get; init; }

    [JsonPropertyName("planned_sets")]
    public int? PlannedSets { get; init; }

    [JsonPropertyName("exercise_items")]
    public List<LogExerciseRequest>? ExerciseItems { get; init; }
}

public sealed record LogExerciseRequest
{
    [JsonPropertyName("exercise_id")]
    public required Guid ExerciseId { get; init; }

    [JsonPropertyName("order_index")]
    public required int OrderIndex { get; init; }

    [JsonPropertyName("planned_reps")]
    public int? PlannedReps { get; init; }

    [JsonPropertyName("planned_duration_sec")]
    public int? PlannedDurationSec { get; init; }

    [JsonPropertyName("planned_rest_sec")]
    public int? PlannedRestSec { get; init; }

    [JsonPropertyName("sets")]
    public List<LogSetRequest>? Sets { get; init; }
}

public sealed record LogSetRequest
{
    [JsonPropertyName("set_index")]
    public required int SetIndex { get; init; }

    [JsonPropertyName("status")]
    [JsonConverter(typeof(WorkoutSetStatusJsonConverter))]
    public required WorkoutSetStatus Status { get; init; }

    [JsonPropertyName("actual_reps")]
    public int? ActualReps { get; init; }

    [JsonPropertyName("actual_duration_sec")]
    public int? ActualDurationSec { get; init; }
}

public sealed record LogWeightRequest
{
    [JsonPropertyName("weight_kg")]
    public required decimal WeightKg { get; init; }

    [JsonPropertyName("recorded_at")]
    public DateTime? RecordedAt { get; init; }
}

public sealed record CreateWorkoutPlanRequest
{
    [JsonPropertyName("day_of_week")]
    public required string DayOfWeek { get; init; }

    [JsonPropertyName("workout_name")]
    public required string WorkoutName { get; init; }

    [JsonPropertyName("duration_minutes")]
    public required int DurationMinutes { get; init; }

    [JsonPropertyName("target_calories_burn")]
    public required int TargetCaloriesBurn { get; init; }

    [JsonPropertyName("exercises")]
    public List<WorkoutPlanExerciseInput>? Exercises { get; init; }
}

public sealed record UpdateWorkoutPlanRequest
{
    [JsonPropertyName("day_of_week")]
    public string? DayOfWeek { get; init; }

    [JsonPropertyName("workout_name")]
    public string? WorkoutName { get; init; }

    [JsonPropertyName("duration_minutes")]
    public int? DurationMinutes { get; init; }

    [JsonPropertyName("target_calories_burn")]
    public int? TargetCaloriesBurn { get; init; }

    [JsonPropertyName("is_completed")]
    public bool? IsCompleted { get; init; }

    [JsonPropertyName("exercises")]
    public List<WorkoutPlanExerciseInput>? Exercises { get; init; }
}

public sealed record WorkoutPlanExerciseInput
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(ExerciseTypeJsonConverter))]
    public required ExerciseType Type { get; init; }

    [JsonPropertyName("sets")]
    public required int Sets { get; init; }

    [JsonPropertyName("reps")]
    public int? Reps { get; init; }

    [JsonPropertyName("duration_sec")]
    public int? DurationSec { get; init; }

    [JsonPropertyName("rest_sec")]
    public int RestSec { get; init; }

    [JsonPropertyName("rest_after_sec")]
    public int? RestAfterSec { get; init; }

    [JsonPropertyName("calories")]
    public int Calories { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("media_url")]
    public string? MediaUrl { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("media_source")]
    public string? MediaSource { get; init; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; init; }
}
