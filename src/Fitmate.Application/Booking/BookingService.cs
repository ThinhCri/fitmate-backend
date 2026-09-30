using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Application.Finance;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Finance;

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
    private readonly IFinancialLedgerStore? _ledger;

    public BookingService(
        IAppointmentStore appointments,
        IBookingHoldStore holds,
        IServiceBookingLookup services,
        IOutboxDispatcher outbox,
        IAuthorizationGuard authz,
        TimeSpan? holdTtl = null,
        IFinancialLedgerStore? ledger = null)
    {
        _appointments = appointments;
        _holds = holds;
        _services = services;
        _outbox = outbox;
        _authz = authz;
        _holdTtl = holdTtl ?? TimeSpan.FromMinutes(5); // domain-rules.md §3.6
        _ledger = ledger;
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

        // 2. Concurrency & Overlap: Lock theo PT và kiểm tra interval overlap [S1, E1) và [S2, E2).
        return await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            if (await _appointments.HasOverlappingSlotAsync(ptUserId, slotStartUtc, svc.DurationMinutes, null, ct))
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
        }, ct);
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

        return await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
        {
            appt.Cancel(reason);
            await _appointments.SaveChangesAsync(ct);
            _outbox.Enqueue("booking.cancelled", new { appointmentId, actorUserId, reason, memberId = appt.MemberId, ptUserId = appt.PtUserId });
            return appt;
        }, ct);
    }

    public async Task<Appointment> ConfirmAsync(Guid ptUserId, Guid appointmentId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.PtUserId != ptUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner PT.");

        return await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            appt.ConfirmByPt();
            await _appointments.SaveChangesAsync(ct);
            _outbox.Enqueue("booking.confirmed", new { appointmentId, ptUserId, memberId = appt.MemberId });
            return appt;
        }, ct);
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

        return await _appointments.ExecuteWithScheduleLockAsync(ptUserId, async () =>
        {
            appt.CompleteByPt();
            await _appointments.SaveChangesAsync(ct);

            if (_ledger != null && appt.ActualPaidVnd > 0 && !await _ledger.HasEntriesForAppointmentAsync(appt.Id, ct))
            {
                var completedAt = appt.CompletedAtUtc ?? DateTime.UtcNow;
                var entries = new[]
                {
                    FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.GrossBookingFee, appt.ActualPaidVnd, completedAt, ptUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"booking.completed:{appt.Id}"),
                    FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.PlatformCommission, appt.PlatformFeeVnd, completedAt, ptUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"booking.completed:{appt.Id}"),
                    FinancialLedgerEntry.Create(FinancialLedgerEntryTypes.TrainerPayable, appt.TrainerShareVnd, completedAt, ptUserId.ToString(), appointmentId: appt.Id, paymentId: appt.PaymentId, sourceEventId: $"booking.completed:{appt.Id}")
                };
                await _ledger.AddEntriesAsync(entries, ct);
                await _ledger.SaveChangesAsync(ct);
            }

            _outbox.Enqueue("booking.completed", new { appointmentId, ptUserId, memberId = appt.MemberId });
            return appt;
        }, ct);
    }

    public async Task<Appointment> RescheduleAsync(Guid memberId, Guid appointmentId, DateTime newSlotStartUtc, CancellationToken ct)
    {
        var appt = await _appointments.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");
        if (appt.MemberId != memberId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner.");

        if (appt.Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.Expired)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot reschedule in status {appt.Status}.");

        if (newSlotStartUtc == appt.SlotStartUtc)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "new_slot_start_utc must differ from current.",
                new Dictionary<string, string[]> { ["new_slot_start_utc"] = new[] { "different from current" } });

        return await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
        {
            if (await _appointments.HasOverlappingSlotAsync(appt.PtUserId, newSlotStartUtc, appt.DurationMinutes, appt.Id, ct))
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
        }, ct);
    }

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByMemberAsync(Guid memberId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
        => _appointments.ListByMemberPagedAsync(memberId, status, page, pageSize, ct);

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListByPtAsync(Guid ptUserId, AppointmentStatus? status, int page, int pageSize, CancellationToken ct)
        => _appointments.ListByPtPagedAsync(ptUserId, status, page, pageSize, ct);

    public Task<(IReadOnlyList<Appointment> Items, int Total)> ListAdminAsync(
        AppointmentStatus? status, Guid? ptUserId, Guid? memberId, int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 25 : (pageSize > 100 ? 100 : pageSize);
        return _appointments.ListAdminPagedAsync(status, ptUserId, memberId, page, pageSize, ct);
    }


    public Task<Appointment?> FindByIdAsync(Guid appointmentId, CancellationToken ct)
        => _appointments.FindByIdAsync(appointmentId, ct);

    public async Task<int> ExpireOldHoldsAndPendingAsync(DateTime nowUtc, CancellationToken ct)
    {
        var expiredHolds = await _appointments.GetExpiredHeldAppointmentsAsync(nowUtc, 50, ct);
        var count = 0;
        foreach (var appt in expiredHolds)
        {
            await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
            {
                appt.ExpireHold();
                await _appointments.SaveChangesAsync(ct);
                _outbox.Enqueue("booking.expired", new { appointmentId = appt.Id, memberId = appt.MemberId, ptUserId = appt.PtUserId, slotStartUtc = appt.SlotStartUtc });
                return true;
            }, ct);
            count++;
        }

        var expiredPending = await _appointments.GetExpiredPendingAppointmentsAsync(nowUtc, 50, ct);
        foreach (var appt in expiredPending)
        {
            await _appointments.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
            {
                appt.ExpireAcceptance();
                await _appointments.SaveChangesAsync(ct);
                _outbox.Enqueue("booking.acceptance_expired", new { appointmentId = appt.Id, memberId = appt.MemberId, ptUserId = appt.PtUserId });
                if (appt.PaymentId.HasValue && appt.ActualPaidVnd > 0)
                {
                    _outbox.Enqueue("finance.refund_required", new { appointmentId = appt.Id, paymentId = appt.PaymentId.Value, amountVnd = appt.ActualPaidVnd, reason = "PT acceptance deadline expired" });
                }
                return true;
            }, ct);
            count++;
        }

        return count;
    }
}
