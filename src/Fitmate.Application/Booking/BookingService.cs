using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Booking;

/// <summary>
/// Default <see cref="IBookingService"/> impl. BE-060 scope.
/// </summary>
/// <remarks>
/// <para><b>Out of scope (BE-160):</b> Confirm/Accept cần Appointment.HoldId FK + AppointmentStatus (DEC-026).
/// Service này chỉ implement create hold, cancel, complete, reschedule, list. Confirm/Accept sẽ bổ sung
/// ở BE-160 khi migrate AppointmentStatus.</para>
/// </remarks>
public class BookingService : IBookingService
{
    private readonly IAppointmentStore _appointments;
    private readonly IBookingHoldStore _holds;
    private readonly IServiceBookingLookup _services;
    private readonly IOutboxDispatcher _outbox;
    private readonly IAuthorizationGuard _authz;
    private readonly TimeSpan _holdTtl;

    public BookingService(
        IAppointmentStore appointments,
        IBookingHoldStore holds,
        IServiceBookingLookup services,
        IOutboxDispatcher outbox,
        IAuthorizationGuard authz,
        TimeSpan? holdTtl = null)
    {
        _appointments = appointments;
        _holds = holds;
        _services = services;
        _outbox = outbox;
        _authz = authz;
        _holdTtl = holdTtl ?? TimeSpan.FromMinutes(5); // domain-rules.md §3.6
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _slotLocks = new();

    public async Task<(BookingHold Hold, Appointment Appointment)> CreateHoldAsync(
        Guid memberId, Guid ptUserId, Guid serviceId, DateTime slotStartUtc, CancellationToken ct)
    {
        // 1. Validate service.
        var svc = await _services.FindActiveAsync(serviceId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Service {serviceId} not found or inactive.");
        if (svc.PtUserId != ptUserId)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Service does not belong to PT.",
                new Dictionary<string, string[]> { ["pt_id"] = new[] { "mismatch" } });

        // 2. Concurrency: Lock theo (ptUserId, slotStartUtc) giải quyết dứt điểm race condition khi nhiều VUs cùng tranh chấp 1 slot.
        var lockKey = $"{ptUserId:N}:{slotStartUtc.Ticks}";
        var sem = _slotLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            if (await _appointments.ExistsActiveForSlotAsync(ptUserId, slotStartUtc, ct))
                throw new ConflictException(ErrorCodes.Booking.SlotTaken,
                    $"Slot {slotStartUtc:O} of PT {ptUserId} is no longer available.");

            // 3. Tạo hold + Appointment(Held).
            var hold = BookingHold.Create(memberId, ptUserId, serviceId, slotStartUtc, _holdTtl);
            var appt = Appointment.CreateHold(
                memberId, ptUserId, serviceId,
                slotStartUtc, slotStartUtc, svc.DurationMinutes, svc.PriceVnd);

            await _holds.AddAsync(hold, ct);
            await _appointments.AddAsync(appt, ct);
            await _appointments.SaveChangesAsync(ct);
            await _holds.SaveChangesAsync(ct);

            _outbox.Enqueue("booking.held", new { holdId = hold.Id, appointmentId = appt.Id, memberId, ptUserId });
            return (hold, appt);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<Appointment> CancelAsync(Guid actorUserId, Guid appointmentId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });

        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.MemberId != actorUserId && appt.PtUserId != actorUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner or PT.");

        appt.Cancel(reason);
        await _appointments.SaveChangesAsync(ct);
        _outbox.Enqueue("booking.cancelled", new { appointmentId, actorUserId, reason });
        return appt;
    }

    public async Task<Appointment> ConfirmAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");

        appt.ConfirmByPt();
        await _appointments.SaveChangesAsync(ct);
        _outbox.Enqueue("booking.confirmed", new { appointmentId, ptUserId, memberId = appt.MemberId });
        return appt;
    }

    public async Task<Appointment> StartAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");

        appt.StartByPt();
        await _appointments.SaveChangesAsync(ct);
        _outbox.Enqueue("booking.started", new { appointmentId, ptUserId, memberId = appt.MemberId });
        return appt;
    }

    public async Task<Appointment> CompleteAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        // BE-090: enforce role PT ở Application.
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");
        appt.CompleteByPt();
        await _appointments.SaveChangesAsync(ct);
        _outbox.Enqueue("booking.completed", new { appointmentId, ptUserId, memberId = appt.MemberId });
        return appt;
    }

    public async Task<Appointment> RescheduleAsync(Guid memberId, Guid appointmentId, DateTime newSlotStartUtc, CancellationToken ct)
    {
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.MemberId != memberId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner.");

        if (appt.Status is BookingStatus.Completed or BookingStatus.Cancelled or BookingStatus.Refunded)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot reschedule in status {appt.Status}.");

        if (newSlotStartUtc == appt.SlotStartUtc)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "new_slot_start_utc must differ from current.",
                new Dictionary<string, string[]> { ["new_slot_start_utc"] = new[] { "different from current" } });

        if (await _appointments.ExistsActiveForSlotAsync(appt.PtUserId, newSlotStartUtc, ct))
            throw new ConflictException(ErrorCodes.Booking.SlotTaken,
                $"New slot {newSlotStartUtc:O} is no longer available.");

        var oldSlot = appt.SlotStartUtc;
        appt.Cancel($"Reschedule from {oldSlot:O} to {newSlotStartUtc:O}");
        await _appointments.SaveChangesAsync(ct);

        // Tạo appointment mới (BE-160 sẽ thêm previous_booking_id field; tạm thời liên kết qua Outbox event).
        var newAppt = Appointment.CreateHold(memberId, appt.PtUserId, appt.ServiceId,
            newSlotStartUtc, newSlotStartUtc, appt.DurationMinutes, appt.PriceListedVnd);
        await _appointments.AddAsync(newAppt, ct);
        await _appointments.SaveChangesAsync(ct);

        _outbox.Enqueue("booking.rescheduled",
            new { oldAppointmentId = appointmentId, newAppointmentId = newAppt.Id, oldSlot, newSlotStartUtc });
        return newAppt;
    }

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberAsync(Guid memberId, BookingStatus? status, int page, int pageSize, CancellationToken ct)
        => _appointments.ListByMemberPagedAsync(memberId, status, page, pageSize, ct);

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtAsync(Guid ptUserId, BookingStatus? status, int page, int pageSize, CancellationToken ct)
        => _appointments.ListByPtPagedAsync(ptUserId, status, page, pageSize, ct);

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminAsync(
        BookingStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 25 : (pageSize > 100 ? 100 : pageSize);
        return _appointments.ListAdminPagedAsync(status, ptUserId, memberId, page, pageSize, ct);
    }

    public Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct)
        => _appointments.FindByIdAsync(appointmentId, ct);
}
