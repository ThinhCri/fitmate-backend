using Fitmate.Domain.Common;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Default <see cref="IServiceOfferingService"/> impl.
/// </summary>
public class ServiceOfferingService : IServiceOfferingService
{
    private readonly IServiceOfferingStore _store;

    public ServiceOfferingService(IServiceOfferingStore store)
    {
        _store = store;
    }

    public async Task<Domain.Trainers.Service> CreateAsync(Guid ptUserId, string name, string? description, long priceVnd, int durationMinutes, CancellationToken ct)
    {
        // Domain.Service.Create đã validate name/price/duration ném ArgumentException;
        // catch + convert sang ValidationException để chuẩn format ProblemDetails.
        Domain.Trainers.Service entity;
        try
        {
            entity = Domain.Trainers.Service.Create(ptUserId, name, description, priceVnd, durationMinutes);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, ex.Message, EmptyErrors());
        }

        await _store.AddAsync(entity, ct);
        await _store.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<Domain.Trainers.Service> UpdateAsync(Guid ptUserId, Guid serviceId, string? name, string? description, long? priceVnd, int? durationMinutes, bool? isActive, CancellationToken ct)
    {
        var service = await _store.FindAsync(serviceId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Service {serviceId} not found.");
        if (service.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Not owner.");

        try
        {
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
