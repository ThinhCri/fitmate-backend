using System.Text.Json.Serialization;
using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Fitmate.Domain.Common;
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
            body.Name, body.Description, body.PriceVnd, body.DurationMinutes, ct,
            RequireDetails(body.ServiceKind, body.DeliveryMode, body.TargetAudience,
                body.RequiredEquipment, body.Deliverable, body.LocationName, body.LocationAddress));
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
            body.Name, body.Description, body.PriceVnd, body.DurationMinutes, body.IsActive, ct,
            HasDetails(body.ServiceKind, body.DeliveryMode, body.TargetAudience, body.RequiredEquipment,
                body.Deliverable, body.LocationName, body.LocationAddress)
                ? RequireDetails(body.ServiceKind, body.DeliveryMode, body.TargetAudience,
                    body.RequiredEquipment, body.Deliverable, body.LocationName, body.LocationAddress)
                : null);
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
        new(s.Id, s.Name, s.Description, s.PriceVnd, s.DurationMinutes, s.IsActive,
            s.Kind == ServiceKind.AssessmentAndPlan ? "assessment_and_plan" : "one_to_one",
            s.DeliveryMode == ServiceDeliveryMode.Online ? "online" : "in_person",
            s.TargetAudience, s.RequiredEquipment, s.Deliverable, s.LocationName, s.LocationAddress);

    private static bool HasDetails(params string?[] fields) => fields.Any(x => x is not null);

    private static ServiceOfferingDetails RequireDetails(string? kind, string? mode, string? audience,
        string? equipment, string? deliverable, string? locationName, string? locationAddress)
    {
        if (kind is not ("one_to_one" or "assessment_and_plan") || mode is not ("in_person" or "online") ||
            string.IsNullOrWhiteSpace(audience) || string.IsNullOrWhiteSpace(equipment) ||
            string.IsNullOrWhiteSpace(deliverable))
            throw new ValidationException("SERVICE_DETAILS_REQUIRED",
                "Service kind, delivery mode, target audience, equipment and deliverable are required.",
                new Dictionary<string, string[]> { ["service_details"] = ["required"] });
        return new ServiceOfferingDetails(kind == "assessment_and_plan" ? ServiceKind.AssessmentAndPlan : ServiceKind.OneToOne,
            mode == "online" ? ServiceDeliveryMode.Online : ServiceDeliveryMode.InPerson,
            audience, equipment, deliverable, locationName, locationAddress);
    }
}

public sealed record ServiceResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_vnd")] long PriceVnd,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("service_kind")] string ServiceKind,
    [property: JsonPropertyName("delivery_mode")] string DeliveryMode,
    [property: JsonPropertyName("target_audience")] string? TargetAudience,
    [property: JsonPropertyName("required_equipment")] string? RequiredEquipment,
    [property: JsonPropertyName("deliverable")] string? Deliverable,
    [property: JsonPropertyName("location_name")] string? LocationName,
    [property: JsonPropertyName("location_address")] string? LocationAddress);

public sealed record CreateServiceRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_vnd")] long PriceVnd,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("service_kind")] string? ServiceKind = null,
    [property: JsonPropertyName("delivery_mode")] string? DeliveryMode = null,
    [property: JsonPropertyName("target_audience")] string? TargetAudience = null,
    [property: JsonPropertyName("required_equipment")] string? RequiredEquipment = null,
    [property: JsonPropertyName("deliverable")] string? Deliverable = null,
    [property: JsonPropertyName("location_name")] string? LocationName = null,
    [property: JsonPropertyName("location_address")] string? LocationAddress = null);

public sealed record UpdateServiceRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_vnd")] long? PriceVnd,
    [property: JsonPropertyName("duration_minutes")] int? DurationMinutes,
    [property: JsonPropertyName("is_active")] bool? IsActive,
    [property: JsonPropertyName("service_kind")] string? ServiceKind = null,
    [property: JsonPropertyName("delivery_mode")] string? DeliveryMode = null,
    [property: JsonPropertyName("target_audience")] string? TargetAudience = null,
    [property: JsonPropertyName("required_equipment")] string? RequiredEquipment = null,
    [property: JsonPropertyName("deliverable")] string? Deliverable = null,
    [property: JsonPropertyName("location_name")] string? LocationName = null,
    [property: JsonPropertyName("location_address")] string? LocationAddress = null);
