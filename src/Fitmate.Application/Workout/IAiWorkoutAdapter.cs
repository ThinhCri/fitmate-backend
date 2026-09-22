namespace Fitmate.Application.Workout;

public sealed record WorkoutSuggestionRequest(
    Guid UserId,
    string? Goal,
    string? FitnessLevel,
    IReadOnlyList<string>? PreferredEquipment = null,
    IReadOnlyList<string>? TargetMuscleGroups = null,
    string? Notes = null);

public sealed record WorkoutSuggestionResultView(
    string SuggestedPlanName,
    string? Description,
    IReadOnlyList<WorkoutSuggestedExerciseView> Exercises,
    decimal? Confidence,
    string Source,
    string? Disclaimer);

public sealed record WorkoutSuggestedExerciseView(
    string ExerciseName,
    int Sets,
    int Reps,
    int? RestSeconds,
    string? Notes);

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

