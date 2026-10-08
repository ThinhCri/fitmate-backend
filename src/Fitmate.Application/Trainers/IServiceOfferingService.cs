using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

public sealed record ServiceOfferingDetails(Domain.Trainers.ServiceKind Kind,
    Domain.Trainers.ServiceDeliveryMode DeliveryMode, string TargetAudience,
    string RequiredEquipment, string Deliverable, string? LocationName, string? LocationAddress);

/// <summary>
/// Use case cho <see cref="Domain.Trainers.Service"/> (dịch vụ của PT).
/// Theo OpenAPI: <c>/me/services</c> (PT tự CRUD).
/// </summary>
public interface IServiceOfferingService
{
    Task<Domain.Trainers.Service> CreateAsync(Guid ptUserId, string name, string? description, long priceVnd, int durationMinutes, CancellationToken ct, ServiceOfferingDetails? details = null);
    Task<Domain.Trainers.Service> UpdateAsync(Guid ptUserId, Guid serviceId, string? name, string? description, long? priceVnd, int? durationMinutes, bool? isActive, CancellationToken ct, ServiceOfferingDetails? details = null);
    Task DeactivateAsync(Guid ptUserId, Guid serviceId, CancellationToken ct);
    Task<Domain.Trainers.Service?> GetAsync(Guid ptUserId, Guid serviceId, CancellationToken ct);
    Task<IReadOnlyList<Domain.Trainers.Service>> ListMineAsync(Guid ptUserId, bool includeInactive, CancellationToken ct);
}
