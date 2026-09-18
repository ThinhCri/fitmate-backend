using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Trainers;

public class EfServiceOfferingStore : IServiceOfferingStore
{
    private readonly FitmateDbContext _db;

    public EfServiceOfferingStore(FitmateDbContext db)
    {
        _db = db;
    }

    public Task<Domain.Trainers.Service?> FindAsync(Guid serviceId, CancellationToken ct)
        => _db.Services.FirstOrDefaultAsync(s => s.Id == serviceId, ct);

    public async Task<IReadOnlyList<Domain.Trainers.Service>> ListByPtAsync(Guid ptUserId, bool includeInactive, CancellationToken ct)
    {
        IQueryable<Domain.Trainers.Service> q = _db.Services.Where(s => s.PtUserId == ptUserId);
        if (!includeInactive) q = q.Where(s => s.IsActive);
        return await q.OrderBy(s => s.CreatedAt).ToListAsync(ct);
    }

    public async Task AddAsync(Domain.Trainers.Service service, CancellationToken ct)
        => await _db.Services.AddAsync(service, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}

public class EfAvailabilitySlotStore : IAvailabilitySlotStore
{
    private readonly FitmateDbContext _db;

    public EfAvailabilitySlotStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AvailabilitySlot>> ListByPtAsync(Guid ptUserId, AvailabilityStatus[] statuses, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        return await _db.AvailabilitySlots
            .Where(s => s.PtUserId == ptUserId && statuses.Contains(s.Status) && s.SlotStartUtc >= fromUtc && s.SlotStartUtc < toUtc)
            .OrderBy(s => s.SlotStartUtc)
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<AvailabilitySlot>> ListOpenByPtAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        => ListByPtAsync(ptUserId, new[] { AvailabilityStatus.Open }, fromUtc, toUtc, ct);

    public async Task DeleteByPtAndStatusAsync(Guid ptUserId, AvailabilityStatus status, CancellationToken ct)
    {
        await _db.AvailabilitySlots
            .Where(s => s.PtUserId == ptUserId && s.Status == status)
            .ExecuteDeleteAsync(ct);
    }

    public async Task AddAsync(AvailabilitySlot slot, CancellationToken ct)
        => await _db.AvailabilitySlots.AddAsync(slot, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
