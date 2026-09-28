using System.Text.Json.Serialization;
using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

/// <summary>
/// BE-050: PT self-service ServiceOffering endpoints (theo OpenAPI <c>/me/services</c>).
/// </summary>
[ApiController]
[Authorize(Roles = "PT")]
[Route("api/v1/me/services")]
public class ServicesController : ControllerBase
{
    private readonly IServiceOfferingService _services;

    public ServicesController(IServiceOfferingService services)
    {
        _services = services;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> List(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] bool include_inactive = false,
        CancellationToken ct = default)
    {
        var items = await _services.ListMineAsync(current.GetUserIdOrThrow(), include_inactive, ct);
        return Ok(items.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse>> Create(
        [FromServices] ICurrentUserAccessor current,
        [FromBody] CreateServiceRequest body,
        CancellationToken ct)
    {
        var s = await _services.CreateAsync(
            current.GetUserIdOrThrow(),
            body.Name, body.Description, body.PriceVnd, body.DurationMinutes, ct);
        return CreatedAtAction(nameof(Get), new { id = s.Id }, ToResponse(s));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServiceResponse>> Get(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        var s = await _services.GetAsync(current.GetUserIdOrThrow(), id, ct);
        return s is null ? NotFound() : Ok(ToResponse(s));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ServiceResponse>> Update(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        [FromBody] UpdateServiceRequest body,
        CancellationToken ct)
    {
        var s = await _services.UpdateAsync(
            current.GetUserIdOrThrow(),
            id,
            body.Name, body.Description, body.PriceVnd, body.DurationMinutes, body.IsActive, ct);
        return Ok(ToResponse(s));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(
        [FromServices] ICurrentUserAccessor current,
        Guid id,
        CancellationToken ct)
    {
        await _services.DeactivateAsync(current.GetUserIdOrThrow(), id, ct);
        return NoContent();
    }

    internal static ServiceResponse ToResponse(Service s) =>
        new(s.Id, s.Name, s.Description, s.PriceVnd, s.DurationMinutes, s.IsActive);
}

public sealed record ServiceResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_vnd")] long PriceVnd,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("is_active")] bool IsActive);

public sealed record CreateServiceRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_vnd")] long PriceVnd,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes);

public sealed record UpdateServiceRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_vnd")] long? PriceVnd,
    [property: JsonPropertyName("duration_minutes")] int? DurationMinutes,
    [property: JsonPropertyName("is_active")] bool? IsActive);
