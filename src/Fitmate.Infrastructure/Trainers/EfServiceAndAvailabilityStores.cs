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

    public async Task<IReadOnlyList<AvailabilitySlot>> ListOpenByPtAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var slots = await ListByPtAsync(ptUserId, new[] { AvailabilityStatus.Open }, fromUtc, toUtc, ct);
        if (slots.Count == 0) return slots;

        var nowUtc = DateTime.UtcNow;
        var activeAppointments = await _db.Appointments
            .Where(a => a.PtUserId == ptUserId &&
                        a.SlotStartUtc < toUtc &&
                        (
                            (a.Status == Domain.Common.AppointmentStatus.Held && (a.HoldExpiresAtUtc == null || a.HoldExpiresAtUtc > nowUtc)) ||
                            a.Status == Domain.Common.AppointmentStatus.Pending ||
                            a.Status == Domain.Common.AppointmentStatus.Confirmed ||
                            a.Status == Domain.Common.AppointmentStatus.InProgress
                        ))
            .Select(a => new { a.SlotStartUtc, a.DurationMinutes })
            .ToListAsync(ct);

        if (activeAppointments.Count == 0) return slots;

        // Loại bỏ khung giờ đã bị trùng hoặc nằm trong ca đã giữ chỗ/đã đặt
        return slots.Where(s =>
        {
            var slotEndUtc = s.SlotStartUtc.AddMinutes(s.DurationMinutes);
            bool isOverlapping = activeAppointments.Any(a =>
                s.SlotStartUtc < a.SlotStartUtc.AddMinutes(a.DurationMinutes) &&
                slotEndUtc > a.SlotStartUtc);
            return !isOverlapping;
        }).ToList();
    }

    public async Task DeleteByPtAndStatusAsync(Guid ptUserId, AvailabilityStatus status, CancellationToken ct)
    {
        await _db.AvailabilitySlots
            .Where(s => s.PtUserId == ptUserId && s.Status == status)
            .ExecuteDeleteAsync(ct);
    }

    public async Task ReplaceScopedAsync(Guid ptUserId, DateTime fromUtc, DateTime toUtc, IReadOnlyList<AvailabilitySlot> newSlots, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                foreach (var slot in newSlots)
                {
                    var entry = _db.Entry(slot);
                    if (entry.State != EntityState.Detached) entry.State = EntityState.Detached;
                }

                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await _db.AvailabilitySlots
                    .Where(s => s.PtUserId == ptUserId && s.Status == AvailabilityStatus.Open && s.SlotStartUtc >= fromUtc && s.SlotStartUtc < toUtc)
                    .ExecuteDeleteAsync(ct);

                if (newSlots.Count > 0)
                {
                    await _db.AvailabilitySlots.AddRangeAsync(newSlots, ct);
                    await _db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);
            });
        }
        else
        {
            var toDelete = await _db.AvailabilitySlots
                .Where(s => s.PtUserId == ptUserId && s.Status == AvailabilityStatus.Open && s.SlotStartUtc >= fromUtc && s.SlotStartUtc < toUtc)
                .ToListAsync(ct);
            _db.AvailabilitySlots.RemoveRange(toDelete);
            if (newSlots.Count > 0)
            {
                await _db.AvailabilitySlots.AddRangeAsync(newSlots, ct);
            }
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<string?> GetPtTimezoneAsync(Guid ptUserId, CancellationToken ct)
    {
        return await _db.Users
            .Where(u => u.Id == ptUserId)
            .Select(u => u.Timezone)
            .FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(AvailabilitySlot slot, CancellationToken ct)
        => await _db.AvailabilitySlots.AddAsync(slot, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}
