using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

/// <summary>
/// Use case Booking state machine theo domain-rules.md §3.2 và DEC-051.
/// </summary>
public interface IBookingService
{
    /// <summary>Member tạo hold cho 1 slot. TTL 5 phút (domain-rules.md §3.6).</summary>
    Task<(BookingHold Hold, Appointment Appointment)> CreateHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId, DateTime slotStartUtc, CancellationToken ct);

    /// <summary>Member/PT cancel. Reason bắt buộc ≥ 5 chars.</summary>
    Task<Appointment> CancelAsync(Guid actorUserId, Guid appointmentId, string reason, CancellationToken ct);

    /// <summary>PT confirm/accept booking (Pending → Confirmed). Domain-rules.md §3.2.</summary>
    Task<Appointment> ConfirmAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);

    /// <summary>PT start session (Confirmed → InProgress). Domain-rules.md §3.2.</summary>
    Task<Appointment> StartAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);

    /// <summary>PT complete booking (InProgress → Completed). Domain-rules.md §3.2.</summary>
    Task<Appointment> CompleteAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);

    /// <summary>List booking của member (phân trang).</summary>
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberAsync(Guid memberId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>List booking mà PT nhận (phân trang).</summary>
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtAsync(Guid ptUserId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>List booking cho Admin (phân trang, filter theo status, PT, member).</summary>
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminAsync(AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct);

    /// <summary>Lookup appointment theo id (cho internal use).</summary>
    Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct);

    /// <summary>Sweep and expire old holds (>5m) and pending appointments whose acceptance deadline has passed.</summary>
    Task<int> ExpireOldHoldsAndPendingAsync(DateTime nowUtc, CancellationToken ct);
}
