using Fitmate.Application.Ai;
using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Workout;

public sealed class WorkoutSuggestionService(IAuthorizationGuard authz, IAiWorkoutAdapter adapter, IAiUsageService usage)
{
    public async Task<WorkoutSuggestionResultView> SuggestAsync(WorkoutSuggestionRequest input, CancellationToken ct)
    {
        await authz.EnsureAnyRoleAsync(input.UserId, ["Member", "PT"], ct);
        if (string.IsNullOrWhiteSpace(input.Goal) || input.Goal.Length > 200 || input.FitnessLevel?.Length > 100 || input.Notes?.Length > 1000 ||
            input.PreferredEquipment?.Count > 20 || input.TargetMuscleGroups?.Count > 20 ||
            (input.PreferredEquipment ?? []).Concat(input.TargetMuscleGroups ?? []).Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 80))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Kiểm tra mục tiêu và thông tin tập luyện.",
                new Dictionary<string, string[]> { ["goal"] = new[] { "Goal required, maximum 200 characters; other inputs have bounded lengths." } });
        // Text generation shares the existing daily chat budget, rather than bypassing usage limits.
        var reservation = await usage.ReserveAsync(input.UserId, "chat", ct);
        try { var result = await adapter.SuggestWorkoutPlanAsync(input, ct); await usage.CommitAsync(reservation, ct); return result; }
        catch { await usage.ReleaseAsync(reservation, CancellationToken.None); throw; }
    }
}
