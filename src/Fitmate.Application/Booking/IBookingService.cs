using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

/// <summary>
/// Use case Booking state machine theo domain-rules.md §3.2.
/// </summary>
/// <remarks>
/// <para>Mapping domain BookingStatus (7 value cũ gộp lịch + payment) sang OpenAPI AppointmentStatus (7 value PascalCase)
/// là task riêng (BE-160 / Migration). Service giữ enum domain hiện tại để không phá schema/DB.</para>
/// <para>Concurrency: <c>CreateHoldAsync</c> insert BookingHold + Appointment(Held) cùng transaction.
/// Nếu slot đã có Appointment/Pending/Held/Paid cho cùng (ptUserId, slotStartUtc) → ConflictException.</para>
/// <para>Idempotency: BE-070 middleware lo phần header. Service không xử lý key.</para>
/// </remarks>
public interface IBookingService
{
    /// <summary>Member tạo hold cho 1 slot. TTL 5 phút (domain-rules.md §3.6).</summary>
    Task<(BookingHold Hold, Appointment Appointment)> CreateHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId, DateTime slotStartUtc, CancellationToken ct);

    /// <summary>Member/PT cancel. Reason bắt buộc ≥ 5 chars.</summary>
    Task<Appointment> CancelAsync(Guid actorUserId, Guid appointmentId, string reason, CancellationToken ct);

    /// <summary>PT confirm/accept booking (Paid/AwaitingPayment → Confirmed). Domain-rules.md §3.2.</summary>
    Task<Appointment> ConfirmAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);

    /// <summary>PT start session (Confirmed/Paid → InProgress). Domain-rules.md §3.2.</summary>
    Task<Appointment> StartAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);

    /// <summary>PT complete booking (chỉ khi InProgress/Confirmed/Paid → Completed). Domain-rules.md §3.2.</summary>
    Task<Appointment> CompleteAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct);

    /// <summary>Member reschedule: kiểm slot mới còn Open, tạo Appointment mới (previous_booking_id = old).</summary>
    Task<Appointment> RescheduleAsync(Guid memberId, Guid appointmentId, DateTime newSlotStartUtc, CancellationToken ct);

    /// <summary>List booking của member (phân trang).</summary>
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberAsync(Guid memberId, BookingStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>List booking mà PT nhận (phân trang).</summary>
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtAsync(Guid ptUserId, BookingStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>List booking cho Admin (phân trang, filter theo status, PT, member).</summary>
    Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminAsync(BookingStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct);

    /// <summary>Lookup appointment theo id (cho internal use).</summary>
    Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct);
}
