using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;

namespace Fitmate.Application.Workout;

/// <summary>
/// Default <see cref="IWeightService"/> impl.
/// </summary>
public class WeightService : IWeightService
{
    private const decimal MinWeightKg = 20m;
    private const decimal MaxWeightKg = 400m;

    private readonly IWeightStore _store;
    private readonly IAuthorizationGuard _authz;

    public WeightService(IWeightStore store, IAuthorizationGuard authz)
    {
        _store = store;
        _authz = authz;
    }

    public async Task<IReadOnlyList<WeightLogView>> ListAsync(
        Guid userId, CancellationToken ct = default)
    {
        var logs = await _store.ListAsync(userId, ct);
        return logs.Select(Map).ToList();
    }

    public async Task<WeightLogView> LogAsync(
        Guid userId, LogWeightRequest request, CancellationToken ct = default)
    {
        if (request.WeightKg < MinWeightKg || request.WeightKg > MaxWeightKg)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                $"weight_kg must be between {MinWeightKg} and {MaxWeightKg}.",
                new Dictionary<string, string[]>
                {
                    ["weight_kg"] = new[] { $"must be {MinWeightKg}..{MaxWeightKg}" }
                });

        if (request.RecordedAt.HasValue && request.RecordedAt.Value > DateTime.UtcNow.AddDays(1))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "recorded_at cannot be in the future.",
                new Dictionary<string, string[]>
                {
                    ["recorded_at"] = new[] { "cannot be more than 1 day in the future" }
                });

        var recordedAt = request.RecordedAt ?? DateTime.UtcNow;
        var log = WeightLog.Create(userId, request.WeightKg, recordedAt);
        await _store.AddAsync(log, ct);
        await _store.SaveChangesAsync(ct);
        return Map(log);
    }

    public async Task DeleteAsync(Guid userId, Guid weightLogId, CancellationToken ct = default)
    {
        var log = await _store.FindByIdAsync(weightLogId, ct);
        if (log is null || log.UserId != userId)
            throw new NotFoundException(
                WorkoutErrorCodes.WeightNotFound,
                $"Weight log {weightLogId} not found.");

        await _store.DeleteAsync(log, ct);
        await _store.SaveChangesAsync(ct);
    }

    private static WeightLogView Map(WeightLog l) =>
        new(l.Id, l.WeightKg, l.RecordedAt);
}
