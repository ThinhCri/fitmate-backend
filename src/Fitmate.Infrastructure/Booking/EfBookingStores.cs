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

    public Task<bool> IsPackageAppointmentAsync(Guid appointmentId, CancellationToken ct)
        => _db.SessionPackageSessions.AnyAsync(s => s.AppointmentId == appointmentId, ct);

    /// <summary>
    /// Slot đang active (không bao gồm Cancelled/Expired/Refunded).
    /// </summary>
    public Task<bool> ExistsActiveForSlotAsync(Guid ptUserId, DateTime slotStartUtc, CancellationToken ct)
        => HasOverlappingSlotAsync(ptUserId, slotStartUtc, 60, null, ct);

    public async Task<bool> HasOverlappingSlotAsync(Guid ptUserId, DateTime requestedStartUtc, int durationMinutes, Guid? excludeAppointmentId, CancellationToken ct)
    {
        var requestedEndUtc = requestedStartUtc.AddMinutes(durationMinutes);
        var nowUtc = DateTime.UtcNow;

        var candidates = await _db.Appointments
            .Where(a => a.PtUserId == ptUserId &&
                        (excludeAppointmentId == null || a.Id != excludeAppointmentId.Value) &&
                        (
                            (a.Status == AppointmentStatus.Held && (a.HoldExpiresAtUtc == null || a.HoldExpiresAtUtc > nowUtc)) ||
                            a.Status == AppointmentStatus.Pending ||
                            a.Status == AppointmentStatus.Confirmed ||
                            a.Status == AppointmentStatus.InProgress
                        ) &&
                        a.SlotStartUtc < requestedEndUtc)
            .Select(a => new { a.SlotStartUtc, a.DurationMinutes })
            .ToListAsync(ct);

        return candidates.Any(a => requestedStartUtc < a.SlotStartUtc.AddMinutes(a.DurationMinutes));
    }

    public async Task<T> ExecuteWithScheduleLockAsync<T>(Guid ptUserId, Func<Task<T>> operation, CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            var result = await operation();
            await _db.SaveChangesAsync(ct);
            return result;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            await _db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO pt_schedule_locks (pt_user_id, locked_at_utc)
                  VALUES ({0}, now())
                  ON CONFLICT (pt_user_id) DO NOTHING;",
                new object[] { ptUserId }, ct);

            await _db.Database.ExecuteSqlRawAsync(
                @"SELECT 1 FROM pt_schedule_locks WHERE pt_user_id = {0} FOR UPDATE;",
                new object[] { ptUserId }, ct);

            var result = await operation();
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid memberId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Appointment> q = _db.Appointments.Where(a => a.MemberId == memberId);
        if (status.HasValue) q = q.Where(a => a.Status == status.Value);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.SlotStartUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Appointment> q = _db.Appointments.Where(a => a.PtUserId == ptUserId);
        if (status.HasValue) q = q.Where(a => a.Status == status.Value);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.SlotStartUtc)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminPagedAsync(
        AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
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



    public async Task<IReadOnlyList<Appointment>> GetExpiredHeldAppointmentsAsync(DateTime nowUtc, int limit, CancellationToken ct)
    {
        return await _db.Appointments
            .Where(a => a.Status == AppointmentStatus.Held && a.HoldExpiresAtUtc != null &&
                        a.HoldExpiresAtUtc <= nowUtc &&
                        !_db.SessionPackageSessions.Any(s => s.AppointmentId == a.Id))
            .OrderBy(a => a.HoldExpiresAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Appointment>> GetExpiredPendingAppointmentsAsync(DateTime nowUtc, int limit, CancellationToken ct)
    {
        return await _db.Appointments
            .Where(a => a.Status == AppointmentStatus.Pending && a.AcceptanceDeadlineUtc != null && a.AcceptanceDeadlineUtc <= nowUtc)
            .OrderBy(a => a.AcceptanceDeadlineUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Appointment>> GetAwaitingConfirmationPastDeadlineAsync(DateTime nowUtc, int limit, CancellationToken ct)
        => await _db.Appointments
            .Where(a => a.Status == AppointmentStatus.AwaitingMemberConfirmation &&
                        a.MemberConfirmationDeadlineUtc != null &&
                        a.MemberConfirmationDeadlineUtc <= nowUtc)
            .OrderBy(a => a.MemberConfirmationDeadlineUtc)
            .Take(limit)
            .ToListAsync(ct);

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

public sealed class EfSessionPackageStore(FitmateDbContext db) : ISessionPackageStore
{
    public async Task AddAsync(SessionPackage package, CancellationToken ct)
        => await db.SessionPackages.AddAsync(package, ct);

    public Task ReloadAsync(SessionPackage package, CancellationToken ct) =>
        db.Entry(package).ReloadAsync(ct);

    public async Task<bool> AllReservationsActiveAsync(SessionPackage package, DateTime nowUtc, CancellationToken ct)
    {
        var appointmentIds = package.Sessions.Select(s => s.AppointmentId).ToArray();
        var holdIds = package.Sessions.Select(s => s.HoldId).ToArray();
        if (appointmentIds.Length != package.SessionCount || holdIds.Distinct().Count() != package.SessionCount)
            return false;
        var appointments = await db.Appointments.AsNoTracking().CountAsync(a =>
            appointmentIds.Contains(a.Id) && a.MemberId == package.MemberId &&
            a.PtUserId == package.PtUserId && a.ServiceId == package.ServiceId &&
            a.Status == AppointmentStatus.Held && a.HoldExpiresAtUtc > nowUtc, ct);
        var holds = await db.BookingHolds.AsNoTracking().CountAsync(h =>
            holdIds.Contains(h.Id) && h.MemberId == package.MemberId &&
            h.PtUserId == package.PtUserId && h.ServiceId == package.ServiceId &&
            h.Status == "active" && h.ExpiresAt > nowUtc, ct);
        return appointments == package.SessionCount && holds == package.SessionCount;
    }
}

public class EfBookingRescheduleStore : IBookingRescheduleStore
{
    private readonly FitmateDbContext _db;

    public EfBookingRescheduleStore(FitmateDbContext db)
    {
        _db = db;
    }

    public Task<BookingRescheduleRequest?> FindByIdAsync(Guid requestId, CancellationToken ct)
        => _db.BookingRescheduleRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);

    public Task<BookingRescheduleRequest?> FindPendingByAppointmentIdAsync(Guid appointmentId, CancellationToken ct)
        => _db.BookingRescheduleRequests.FirstOrDefaultAsync(r => r.AppointmentId == appointmentId && r.Status == RescheduleRequestStatus.Pending, ct);

    public async Task AddAsync(BookingRescheduleRequest request, CancellationToken ct)
        => await _db.BookingRescheduleRequests.AddAsync(request, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}

/// <summary>Service lookup từ Application store hiện có (IServiceOfferingStore ở BE-050).</summary>
public class ServiceBookingLookup : IServiceBookingLookup
{
    private readonly Fitmate.Application.Trainers.IServiceOfferingStore _store;
    private readonly FitmateDbContext _db;

    private readonly Fitmate.Application.Trainers.IGoogleMeetConnectionService? _meet;

    public ServiceBookingLookup(Fitmate.Application.Trainers.IServiceOfferingStore store, FitmateDbContext db,
        Fitmate.Application.Trainers.IGoogleMeetConnectionService? meet = null)
    {
        _store = store;
        _db = db;
        _meet = meet;
    }

    public async Task<ServiceSnapshot?> FindBookableAsync(Guid serviceId, DateTime slotStartUtc, CancellationToken ct)
    {
        var s = await _store.FindAsync(serviceId, ct);
        if (s is null || !s.IsActive ||
            string.IsNullOrWhiteSpace(s.Description) || string.IsNullOrWhiteSpace(s.TargetAudience) ||
            string.IsNullOrWhiteSpace(s.RequiredEquipment) || string.IsNullOrWhiteSpace(s.Deliverable) ||
            (s.DeliveryMode == Fitmate.Domain.Trainers.ServiceDeliveryMode.InPerson &&
                (string.IsNullOrWhiteSpace(s.LocationName) || string.IsNullOrWhiteSpace(s.LocationAddress)))) return null;
        if (s.DeliveryMode == Fitmate.Domain.Trainers.ServiceDeliveryMode.Online &&
            (_meet is null || !(await _meet.GetStatusAsync(s.PtUserId, ct)).Connected)) return null;
        var bookable = await _db.PtProfiles.AnyAsync(p => p.UserId == s.PtUserId &&
            p.VerificationStatus == Fitmate.Domain.Trainers.PtVerificationStatus.Approved &&
            !p.PermanentlyBanned &&
            (p.SuspendedUntilUtc == null || p.SuspendedUntilUtc <= DateTime.UtcNow), ct) &&
            await _db.Certificates.AnyAsync(c => c.PtUserId == s.PtUserId &&
                c.Status == Fitmate.Domain.Common.CertificateStatus.Approved, ct) &&
            await _db.Users.AnyAsync(u => u.Id == s.PtUserId &&
                u.Status == Fitmate.Domain.Common.UserStatus.Active &&
                u.EmailConfirmed && (u.PhoneNumberConfirmed ||
                    _db.UserProfiles.Any(up => up.UserId == u.Id && up.Phone != null && up.Phone != "")), ct);
        if (!bookable || !await _db.AvailabilitySlots.AnyAsync(a => a.PtUserId == s.PtUserId &&
            a.SlotStartUtc == slotStartUtc && a.DurationMinutes >= s.DurationMinutes &&
            a.Status == Fitmate.Domain.Common.AvailabilityStatus.Open, ct)) return null;
        return new ServiceSnapshot(s.Id, s.PtUserId, s.PriceVnd, s.DurationMinutes, s.IsActive,
            s.DeliveryMode, s.Package4PriceVnd, s.Package8PriceVnd);
    }
}
