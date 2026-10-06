using Fitmate.Api.Auth;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Workout;

/// <summary>
/// BE-160: workout session and weight log endpoints.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/me")]
public class WorkoutsController : ControllerBase
{
    private readonly IWorkoutService _workout;
    private readonly IWeightService _weight;

    public WorkoutsController(IWorkoutService workout, IWeightService weight)
    {
        _workout = workout;
        _weight = weight;
    }

    /// <summary>List the authenticated user's workout plans.</summary>
    [HttpGet("workout/plans")]
    public async Task<IActionResult> ListPlans(
        [FromServices] ICurrentUserAccessor current,
        CancellationToken ct)
    {
        var items = await _workout.ListPlansAsync(current.GetUserIdOrThrow(), ct);
        return Ok(new WorkoutPlanListResponse(items));
    }

    /// <summary>Get a single workout plan with its exercises.</summary>
    [HttpGet("workout/plans/{id:guid}")]
    public async Task<IActionResult> GetPlan(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var detail = await _workout.GetPlanDetailAsync(current.GetUserIdOrThrow(), id, ct);
        if (detail is null)
            return NotFound();
        return Ok(detail);
    }

    /// <summary>Create a new workout plan with exercises.</summary>
    [HttpPost("workout/plans")]
    public async Task<IActionResult> CreatePlan(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] CreateWorkoutPlanRequest body,
        CancellationToken ct)
    {
        var view = await _workout.CreatePlanAsync(current.GetUserIdOrThrow(), body, ct);
        return CreatedAtAction(nameof(GetPlan), new { id = view.Id }, view);
    }

    /// <summary>Update an existing workout plan.</summary>
    [HttpPatch("workout/plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromBody] UpdateWorkoutPlanRequest body,
        CancellationToken ct)
    {
        var view = await _workout.UpdatePlanAsync(current.GetUserIdOrThrow(), id, body, ct);
        return Ok(view);
    }

    /// <summary>Delete a workout plan. Historical sessions are preserved.</summary>
    [HttpDelete("workout/plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        await _workout.DeletePlanAsync(current.GetUserIdOrThrow(), id, ct);
        return NoContent();
    }

    /// <summary>List the authenticated user's workout sessions.</summary>
    [HttpGet("workout/sessions")]
    public async Task<IActionResult> ListSessions(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var items = await _workout.ListSessionsAsync(current.GetUserIdOrThrow(), from, to, ct);
        return Ok(new WorkoutSessionListResponse(items));
    }

    /// <summary>Get a single session with exercise breakdown and set results.</summary>
    [HttpGet("workout/sessions/{id:guid}")]
    public async Task<IActionResult> GetSession(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var detail = await _workout.GetSessionDetailAsync(current.GetUserIdOrThrow(), id, ct);
        if (detail is null)
            return NotFound();
        return Ok(detail);
    }

    /// <summary>Log a completed workout session. Requires Idempotency-Key header.</summary>
    [HttpPost("workout/sessions")]
    public async Task<IActionResult> LogSession(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] LogWorkoutSessionRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "Idempotency-Key header is required.",
                new Dictionary<string, string[]> { ["idempotency_key"] = new[] { "required" } });

        var view = await _workout.LogSessionAsync(current.GetUserIdOrThrow(), body, idempotencyKey, ct);
        return CreatedAtAction(nameof(GetSession), new { id = view.Id }, view);
    }

    /// <summary>List weight log entries.</summary>
    [HttpGet("weight")]
    public async Task<IActionResult> ListWeight(
        [FromServices] ICurrentUserAccessor current,
        CancellationToken ct)
    {
        var items = await _weight.ListAsync(current.GetUserIdOrThrow(), ct);
        return Ok(new WeightLogListResponse(items));
    }

    /// <summary>Log a weight entry.</summary>
    [HttpPost("weight")]
    public async Task<IActionResult> LogWeight(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] LogWeightRequest body,
        CancellationToken ct)
    {
        var view = await _weight.LogAsync(current.GetUserIdOrThrow(), body, ct);
        return Created($"/api/v1/me/weight/{view.Id}", view);
    }

    /// <summary>Soft-delete an owned weight entry.</summary>
    [HttpDelete("weight/{id:guid}")]
    public async Task<IActionResult> DeleteWeight(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        await _weight.DeleteAsync(current.GetUserIdOrThrow(), id, ct);
        return NoContent();
    }
}
