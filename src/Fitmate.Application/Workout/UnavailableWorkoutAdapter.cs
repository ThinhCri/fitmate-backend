using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Workout;

/// <summary>
/// Fail-closed unavailable adapter when no real provider is selected.
/// Validates input, cancellation, and config gating; throws typed 503 AiUnavailableException.
/// </summary>
public class UnavailableWorkoutAdapter : IAiWorkoutAdapter
{
    private readonly IOptions<AiOptions>? _options;

    public UnavailableWorkoutAdapter(IOptions<AiOptions>? options = null)
    {
        _options = options;
    }

    public Task<WorkoutSuggestionResultView> SuggestWorkoutPlanAsync(
        WorkoutSuggestionRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.UserId == Guid.Empty)
        {
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "UserId is required for workout plan suggestions.",
                new Dictionary<string, string[]>
                {
                    ["userId"] = new[] { "UserId must be a valid non-empty GUID." }
                });
        }

        var opt = _options?.Value;
        if (opt is not null && opt.Enabled && opt.WorkoutAdjustEnabled)
        {
            throw new AiUnavailableException("AI workout provider is not configured.");
        }

        throw new AiUnavailableException("AI workout plan suggestion is currently disabled or unavailable awaiting model handoff.");
    }
}
