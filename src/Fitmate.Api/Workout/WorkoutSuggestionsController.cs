using System.Text.Json.Serialization;
using Fitmate.Api.Auth;
using Fitmate.Application.Workout;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Workout;

[ApiController]
[Authorize(Roles = "Member,PT")]
[Route("api/v1/me/workout/suggestions")]
public sealed class WorkoutSuggestionsController(WorkoutSuggestionService suggestions) : ControllerBase
{
    public sealed record Input(
        [property: JsonPropertyName("goal")] string Goal,
        [property: JsonPropertyName("fitness_level")] string? FitnessLevel,
        [property: JsonPropertyName("equipment")] IReadOnlyList<string>? Equipment,
        [property: JsonPropertyName("target_muscles")] IReadOnlyList<string>? TargetMuscles,
        [property: JsonPropertyName("notes")] string? Notes);
    [HttpPost]
    public async Task<IActionResult> Suggest([FromServices] ICurrentUserAccessor current, [FromBody] Input input, CancellationToken ct) =>
        Ok(await suggestions.SuggestAsync(new WorkoutSuggestionRequest(current.GetUserIdOrThrow(), input.Goal, input.FitnessLevel,
            input.Equipment, input.TargetMuscles, input.Notes), ct));
}
