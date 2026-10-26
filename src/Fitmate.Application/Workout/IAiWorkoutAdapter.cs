using System.Text.Json.Serialization;
namespace Fitmate.Application.Workout;

public sealed record WorkoutSuggestionRequest(
    Guid UserId,
    string? Goal,
    string? FitnessLevel,
    IReadOnlyList<string>? PreferredEquipment = null,
    IReadOnlyList<string>? TargetMuscleGroups = null,
    string? Notes = null);

public sealed record WorkoutSuggestionResultView(
    [property: JsonPropertyName("suggested_plan_name")] string SuggestedPlanName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("exercises")] IReadOnlyList<WorkoutSuggestedExerciseView> Exercises,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("disclaimer")] string? Disclaimer);

public sealed record WorkoutSuggestedExerciseView(
    [property: JsonPropertyName("exercise_name")] string ExerciseName,
    [property: JsonPropertyName("sets")] int Sets,
    [property: JsonPropertyName("reps")] int Reps,
    [property: JsonPropertyName("rest_seconds")] int? RestSeconds,
    [property: JsonPropertyName("notes")] string? Notes);

/// <summary>
/// BE-817 / ai-contract.md §2: Boundary for AI workout suggestion and plan adjustments.
/// Explicit suggestion operation only; results require user acceptance before updating plans.
/// Routine session logging does NOT trigger inference.
/// </summary>
public interface IAiWorkoutAdapter
{
    Task<WorkoutSuggestionResultView> SuggestWorkoutPlanAsync(
        WorkoutSuggestionRequest request,
        CancellationToken ct);
}

