using Fitmate.Application.Booking;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Booking;

public class EfAppointmentStore : IAppointmentStore
{
    private readonly FitmateDbContext _db;

    public EfAppointmentStore(FitmateDbContext db)
    {
        _db = db;
    }

    public Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct)
        => _db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

    /// <summary>
    /// Slot đang active (không bao gồm Cancelled/Expired/Refunded).
    /// </summary>
    public Task<bool> ExistsActiveForSlotAsync(Guid ptUserId, DateTime slotStartUtc, CancellationToken ct)
        => _db.Appointments.AnyAsync(a =>
            a.PtUserId == ptUserId &&
            a.SlotStartUtc == slotStartUtc &&
            (a.Status == BookingStatus.Held ||
             a.Status == BookingStatus.AwaitingPayment ||
             a.Status == BookingStatus.Paid ||
             a.Status == BookingStatus.Completed), ct);

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid memberId, BookingStatus? status, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Appointment> q = _db.Appointments.Where(a => a.MemberId == memberId);
        if (status.HasValue) q = q.Where(a => a.Status == status.Value);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.SlotStartUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, BookingStatus? status, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Appointment> q = _db.Appointments.Where(a => a.PtUserId == ptUserId);
        if (status.HasValue) q = q.Where(a => a.Status == status.Value);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.SlotStartUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminPagedAsync(
        BookingStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Appointment> q = _db.Appointments;
        if (status.HasValue) q = q.Where(a => a.Status == status.Value);
        if (ptUserId.HasValue) q = q.Where(a => a.PtUserId == ptUserId.Value);
        if (memberId.HasValue) q = q.Where(a => a.MemberId == memberId.Value);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.SlotStartUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task AddAsync(Appointment appointment, CancellationToken ct)
        => await _db.Appointments.AddAsync(appointment, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}

public class EfBookingHoldStore : IBookingHoldStore
{
    private readonly FitmateDbContext _db;

    public EfBookingHoldStore(FitmateDbContext db)
    {
        _db = db;
    }

    public Task<BookingHold?> FindByIdAsync(Guid holdId, CancellationToken ct)
        => _db.BookingHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);

    public async Task AddAsync(BookingHold hold, CancellationToken ct)
        => await _db.BookingHolds.AddAsync(hold, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}

/// <summary>Service lookup từ Application store hiện có (IServiceOfferingStore ở BE-050).</summary>
public class ServiceBookingLookup : IServiceBookingLookup
{
    private readonly Fitmate.Application.Trainers.IServiceOfferingStore _store;

    public ServiceBookingLookup(Fitmate.Application.Trainers.IServiceOfferingStore store)
    {
        _store = store;
    }

    public async Task<ServiceSnapshot?> FindActiveAsync(Guid serviceId, CancellationToken ct)
    {
        var s = await _store.FindAsync(serviceId, ct);
        if (s is null || !s.IsActive) return null;
        return new ServiceSnapshot(s.Id, s.PtUserId, s.PriceVnd, s.DurationMinutes, s.IsActive);
    }
}
