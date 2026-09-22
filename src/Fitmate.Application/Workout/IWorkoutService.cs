namespace Fitmate.Application.Workout;

/// <summary>
/// Application interface for workout session and exercise log management.
/// Authorization (ownership) is enforced inside this service using the passed
/// <paramref name="userId"/> — callers are responsible for extracting the
/// authenticated user id from <see cref="Auth.ICurrentUserAccessor"/>.
/// </summary>
public interface IWorkoutService
{
    /// <summary>
    /// List workout plans for the given user.
    /// </summary>
    Task<IReadOnlyList<WorkoutPlanView>> ListPlansAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Get a workout plan with its exercises.
    /// Returns null if not found or not owned by the user.
    /// </summary>
    Task<WorkoutPlanDetailView?> GetPlanDetailAsync(
        Guid userId,
        Guid planId,
        CancellationToken ct = default);

    /// <summary>
    /// Create a new workout plan with optional exercises.
    /// </summary>
    Task<WorkoutPlanDetailView> CreatePlanAsync(
        Guid userId,
        CreateWorkoutPlanRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Update an existing workout plan and its exercises.
    /// </summary>
    Task<WorkoutPlanDetailView> UpdatePlanAsync(
        Guid userId,
        Guid planId,
        UpdateWorkoutPlanRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a workout plan and its exercises.
    /// Existing workout sessions preserve their historical snapshots.
    /// </summary>
    Task DeletePlanAsync(
        Guid userId,
        Guid planId,
        CancellationToken ct = default);

    /// <summary>
    /// List workout sessions for the given user with optional date range filter.
    /// Items are ordered by <c>Date DESC, CreatedAt DESC</c>.
    /// </summary>
    Task<IReadOnlyList<WorkoutSessionView>> ListSessionsAsync(
        Guid userId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default);

    /// <summary>
    /// Get a single session with its exercise breakdown and set results.
    /// Returns null if not found or not owned by the user.
    /// </summary>
    Task<WorkoutSessionDetailView?> GetSessionDetailAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Log a completed workout session. Idempotent via <paramref name="idempotencyKey"/>.
    /// </summary>
    Task<WorkoutSessionView> LogSessionAsync(
        Guid userId,
        LogWorkoutSessionRequest request,
        string idempotencyKey,
        CancellationToken ct = default);
}
