using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

/// <summary>
/// Application interface for nutrition tracking: meal log CRUD, meal schedule, and AI scan stub.
/// Authorization (ownership) is enforced inside this service using the passed
/// <paramref name="userId"/> — callers are responsible for extracting the
/// authenticated user id from <see cref="Auth.ICurrentUserAccessor"/>.
/// </summary>
public interface INutritionService
{
    /// <summary>
    /// List meal logs for the given user within an optional date range.
    /// Items are ordered by <c>LoggedAt DESC</c>.
    /// </summary>
    Task<IReadOnlyList<MealLogView>> ListMealsAsync(
        Guid userId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default);

    /// <summary>
    /// Log a new meal entry.
    /// </summary>
    Task<MealLogView> LogMealAsync(
        Guid userId,
        LogMealRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Update or confirm an existing meal log. Idempotent: confirming an already-confirmed meal is a no-op.
    /// Returns null if the meal does not exist or belongs to another user (404 semantics).
    /// </summary>
    Task<MealLogView?> UpdateMealAsync(
        Guid userId,
        Guid mealId,
        UpdateMealRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get the user's current meal schedule. Returns a blank schedule if none exists yet.
    /// </summary>
    Task<MealScheduleView> GetScheduleAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Upsert (replace) the user's meal schedule.
    /// </summary>
    Task<MealScheduleView> UpsertScheduleAsync(
        Guid userId,
        UpsertScheduleRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Scan a meal image and estimate nutrition via AI. Per DEC-016, this returns
    /// 503 when the AI provider is not configured. The actual HTTP response (503 vs 200)
    /// is produced by the controller layer based on the exception thrown here.
    /// </summary>
    /// <exception cref="DomainException">
    /// Throws when the AI provider is unavailable (error code <see cref="ErrorCodes.AI.Unavailable"/>).
    /// </exception>
    Task<NutritionScanResultView> ScanMealAsync(
        Guid userId,
        Stream imageStream,
        string contentType,
        CancellationToken ct = default);
}
