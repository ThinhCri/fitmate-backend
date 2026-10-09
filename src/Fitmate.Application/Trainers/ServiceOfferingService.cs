using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Default <see cref="IServiceOfferingService"/> impl.
/// </summary>
public class ServiceOfferingService : IServiceOfferingService
{
    private readonly IServiceOfferingStore _store;
    private readonly IAuthorizationGuard _authz;
    private readonly IGoogleMeetConnectionService? _meet;

    public ServiceOfferingService(IServiceOfferingStore store, IAuthorizationGuard authz,
        IGoogleMeetConnectionService? meet = null)
    {
        _store = store;
        _authz = authz;
        _meet = meet;
    }

    public async Task<Domain.Trainers.Service> CreateAsync(Guid ptUserId, string name, string? description, long priceVnd, int durationMinutes, CancellationToken ct, ServiceOfferingDetails? details = null)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct); // BE-090
        // Domain.Service.Create đã validate name/price/duration ném ArgumentException;
        // catch + convert sang ValidationException để chuẩn format ProblemDetails.
        Domain.Trainers.Service entity;
        try
        {
            entity = Domain.Trainers.Service.Create(ptUserId, name, description, priceVnd, durationMinutes);
            if (details is not null)
            {
                if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 20)
                    throw new ArgumentException("Describe what the member will do in at least 20 characters.");
                entity.ConfigureDetails(details.Kind, details.DeliveryMode, details.TargetAudience,
                    details.RequiredEquipment, details.Deliverable, details.LocationName, details.LocationAddress);
            }
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, ex.Message, EmptyErrors());
        }

        await _store.AddAsync(entity, ct);
        await _store.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<Domain.Trainers.Service> UpdateAsync(Guid ptUserId, Guid serviceId, string? name, string? description, long? priceVnd, int? durationMinutes, bool? isActive, CancellationToken ct, ServiceOfferingDetails? details = null)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct); // BE-090
        var service = await _store.FindAsync(serviceId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Service {serviceId} not found.");
        if (service.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Not owner.");

        try
        {
            var mode = details?.DeliveryMode ?? service.DeliveryMode;
            if (isActive == true && mode == Domain.Trainers.ServiceDeliveryMode.Online &&
                (_meet is null || !(await _meet.GetStatusAsync(ptUserId, ct)).Connected))
                throw new ArgumentException("Connect Google Meet before publishing an online service.");
            if (isActive == true && details is null && string.IsNullOrWhiteSpace(service.TargetAudience))
                throw new ArgumentException("Service cannot be published until delivery details and Google Meet connection are ready.");
            if (details is not null)
            {
                if (string.IsNullOrWhiteSpace(description ?? service.Description) || (description ?? service.Description)!.Trim().Length < 20)
                    throw new ArgumentException("Describe what the member will do in at least 20 characters.");
                service.ConfigureDetails(details.Kind, details.DeliveryMode, details.TargetAudience,
                    details.RequiredEquipment, details.Deliverable, details.LocationName, details.LocationAddress);
            }
            service.Update(name, description, priceVnd, durationMinutes, isActive);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, ex.Message, EmptyErrors());
        }
        await _store.SaveChangesAsync(ct);
        return service;
    }

    public async Task DeactivateAsync(Guid ptUserId, Guid serviceId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct); // BE-090
        var service = await _store.FindAsync(serviceId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Service {serviceId} not found.");
        if (service.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Not owner.");

        service.Deactivate();
        await _store.SaveChangesAsync(ct);
    }

    public async Task<Domain.Trainers.Service?> GetAsync(Guid ptUserId, Guid serviceId, CancellationToken ct)
    {
        var service = await _store.FindAsync(serviceId, ct);
        if (service is null) return null;
        if (service.PtUserId != ptUserId) return null; // owner-scoped; không leak existence
        return service;
    }

    public Task<IReadOnlyList<Domain.Trainers.Service>> ListMineAsync(Guid ptUserId, bool includeInactive, CancellationToken ct)
        => _store.ListByPtAsync(ptUserId, includeInactive, ct);

    private static Dictionary<string, string[]> EmptyErrors() => new();
}
