using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

/// <summary>
/// Application abstraction cho Appointment persistence.
/// </summary>
public interface IAppointmentStore
{
    Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct);
    Task<bool> ExistsActiveForSlotAsync(Guid ptUserId, DateTime slotStartUtc, CancellationToken ct);
    Task<bool> HasOverlappingSlotAsync(Guid ptUserId, DateTime requestedStartUtc, int durationMinutes, Guid? excludeAppointmentId, CancellationToken ct);
    Task<T> ExecuteWithScheduleLockAsync<T>(Guid ptUserId, Func<Task<T>> operation, CancellationToken ct);
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid memberId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct);
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct);
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminPagedAsync(AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<Appointment>> GetExpiredHeldAppointmentsAsync(DateTime nowUtc, int limit, CancellationToken ct);
    Task<IReadOnlyList<Appointment>> GetExpiredPendingAppointmentsAsync(DateTime nowUtc, int limit, CancellationToken ct);
    Task<IReadOnlyList<Appointment>> GetAwaitingConfirmationPastDeadlineAsync(DateTime nowUtc, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Appointment>>(Array.Empty<Appointment>());
    Task AddAsync(Appointment appointment, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Application abstraction cho BookingHold persistence.</summary>
public interface IBookingHoldStore
{
    Task<BookingHold?> FindByIdAsync(Guid holdId, CancellationToken ct);
    Task AddAsync(BookingHold hold, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Application abstraction cho BookingRescheduleRequest persistence.</summary>
public interface IBookingRescheduleStore
{
    Task<BookingRescheduleRequest?> FindByIdAsync(Guid requestId, CancellationToken ct);
    Task<BookingRescheduleRequest?> FindPendingByAppointmentIdAsync(Guid appointmentId, CancellationToken ct);
    Task AddAsync(BookingRescheduleRequest request, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>Lookup Service cho Booking (price + duration + trainer timezone).</summary>
public interface IServiceBookingLookup
{
    Task<ServiceSnapshot?> FindBookableAsync(Guid serviceId, DateTime slotStartUtc, CancellationToken ct);
}

public sealed record ServiceSnapshot(
    Guid Id,
    Guid PtUserId,
    long PriceVnd,
    int DurationMinutes,
    bool IsActive);
