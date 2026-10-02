using Fitmate.Api.Auth;
using Fitmate.Application.Nutrition;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Nutrition;

[ApiController]
[Authorize]
[Route("api/v1/me/nutrition")]
public class NutritionController : ControllerBase
{
    private readonly INutritionService _nutrition;

    public NutritionController(INutritionService nutrition) => _nutrition = nutrition;

    [HttpGet("meals")]
    public async Task<IActionResult> ListMeals(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var fromUtc = from?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtcExclusive = to?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var meals = await _nutrition.ListMealsAsync(
            current.GetUserIdOrThrow(), fromUtc, toUtcExclusive, ct);
        return Ok(meals);
    }

    [HttpPost("meals")]
    public async Task<IActionResult> LogMeal(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] LogMealRequest body,
        CancellationToken ct)
    {
        if (body.Calories < 0)
            throw new DomainException("FITMATE.VALIDATION.NON_NEGATIVE", "Calories must be non-negative.", 400);
        if (body.ProteinG < 0)
            throw new DomainException("FITMATE.VALIDATION.NON_NEGATIVE", "Protein must be non-negative.", 400);
        if (body.CarbsG < 0)
            throw new DomainException("FITMATE.VALIDATION.NON_NEGATIVE", "Carbs must be non-negative.", 400);
        if (body.FatG < 0)
            throw new DomainException("FITMATE.VALIDATION.NON_NEGATIVE", "Fat must be non-negative.", 400);

        var meal = await _nutrition.LogMealAsync(current.GetUserIdOrThrow(), body, ct);
        return CreatedAtAction(nameof(ListMeals), meal);
    }

    [HttpPatch("meals/{id:guid}")]
    public async Task<IActionResult> UpdateMeal(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromBody] UpdateMealRequest body,
        CancellationToken ct)
    {
        var updated = await _nutrition.UpdateMealAsync(current.GetUserIdOrThrow(), id, body, ct);
        if (updated is null)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = "Meal not found or you do not have permission to update it.",
                Extensions = { ["code"] = NutritionErrorCodes.MealNotFound }
            });
        return Ok(updated);
    }

    [HttpDelete("meals/{id:guid}")]
    public async Task<IActionResult> DeleteMeal(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        await _nutrition.DeleteMealAsync(current.GetUserIdOrThrow(), id, ct);
        return NoContent();
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule(
        [FromServices] ICurrentUserAccessor current,
        CancellationToken ct)
    {
        var schedule = await _nutrition.GetScheduleAsync(current.GetUserIdOrThrow(), ct);
        return Ok(schedule);
    }

    [HttpPut("schedule")]
    public async Task<IActionResult> UpsertSchedule(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] UpsertScheduleRequest body,
        CancellationToken ct)
    {
        var schedule = await _nutrition.UpsertScheduleAsync(current.GetUserIdOrThrow(), body, ct);
        return Ok(schedule);
    }

    [HttpPost("scan")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ScanMeal(
        [FromServices] ICurrentUserAccessor current,
        IFormFile file,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await _nutrition.ScanMealAsync(
            current.GetUserIdOrThrow(), stream, file.ContentType, ct);
        return Ok(result);
    }
}
