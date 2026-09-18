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
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberPagedAsync(Guid memberId, BookingStatus? status, int page, int pageSize, CancellationToken ct);
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, BookingStatus? status, int page, int pageSize, CancellationToken ct);
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

/// <summary>Lookup Service cho Booking (price + duration + trainer timezone).</summary>
public interface IServiceBookingLookup
{
    Task<ServiceSnapshot?> FindActiveAsync(Guid serviceId, CancellationToken ct);
}

public sealed record ServiceSnapshot(
    Guid Id,
    Guid PtUserId,
    long PriceVnd,
    int DurationMinutes,
    bool IsActive);
