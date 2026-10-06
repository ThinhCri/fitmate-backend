using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Workout;

[ApiController]
[Authorize]
[Route("api/v1/exercises")]
public sealed class ExerciseCatalogController : ControllerBase
{
    private readonly IExerciseCatalogService _service;

    public ExerciseCatalogController(IExerciseCatalogService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] string? muscle,
        [FromQuery] string? equipment,
        [FromQuery] string? difficulty,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken ct = default) =>
        Ok(await _service.SearchAsync(query, muscle, equipment, difficulty, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Find(Guid id, CancellationToken ct)
    {
        var item = await _service.FindAsync(id, ct);
        if (item is null)
            throw new NotFoundException(
                WorkoutErrorCodes.ExerciseNotFound,
                $"Exercise catalog item {id} was not found.");
        return Ok(item);
    }
}
