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

        var recordedAt = request.RecordedAt ?? DateTime.UtcNow;
        var log = WeightLog.Create(userId, request.WeightKg, recordedAt);
        await _store.AddAsync(log, ct);
        await _store.SaveChangesAsync(ct);
        return Map(log);
    }

    private static WeightLogView Map(WeightLog l) =>
        new(l.Id, l.WeightKg, l.RecordedAt);
}
