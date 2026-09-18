using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>Application abstraction cho Service persistence.</summary>
public interface IServiceOfferingStore
{
    Task<Domain.Trainers.Service?> FindAsync(Guid serviceId, CancellationToken ct);
    Task<IReadOnlyList<Domain.Trainers.Service>> ListByPtAsync(Guid ptUserId, bool includeInactive, CancellationToken ct);
    Task AddAsync(Domain.Trainers.Service service, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Application abstraction cho AvailabilitySlot persistence.</summary>
public interface IAvailabilitySlotStore
{
    Task<IReadOnlyList<AvailabilitySlot>> ListByPtAsync(Guid ptUserId, AvailabilityStatus[] statuses, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<IReadOnlyList<AvailabilitySlot>> ListOpenByPtAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task DeleteByPtAndStatusAsync(Guid ptUserId, AvailabilityStatus status, CancellationToken ct);
    Task AddAsync(AvailabilitySlot slot, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
