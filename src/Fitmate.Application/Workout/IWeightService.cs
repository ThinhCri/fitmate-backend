namespace Fitmate.Application.Workout;

/// <summary>
/// Application interface for weight log management.
/// Authorization (ownership) is enforced inside this service using the passed
/// <paramref name="userId"/> — callers are responsible for extracting the
/// authenticated user id from <see cref="Auth.ICurrentUserAccessor"/>.
/// </summary>
public interface IWeightService
{
    /// <summary>
    /// List weight log entries for the given user ordered by
    /// <c>RecordedAt DESC</c>.
    /// </summary>
    Task<IReadOnlyList<WeightLogView>> ListAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Log a weight entry. Creates or updates based on whether a record
    /// already exists for the given timestamp (if <paramref name="recordedAt"/>
    /// is provided).
    /// </summary>
    Task<WeightLogView> LogAsync(
        Guid userId,
        LogWeightRequest request,
        CancellationToken ct = default);
}
