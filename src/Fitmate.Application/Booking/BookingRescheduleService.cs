using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fitmate.Application.Common;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Booking;

public class BookingRescheduleService : IBookingRescheduleService
{
    private readonly IAppointmentStore _appointmentStore;
    private readonly IBookingRescheduleStore _rescheduleStore;
    private readonly IOutboxDispatcher _outbox;
    private readonly IOptions<RescheduleOptions> _options;

    public BookingRescheduleService(
        IAppointmentStore appointmentStore,
        IBookingRescheduleStore rescheduleStore,
        IOutboxDispatcher outbox,
        IOptions<RescheduleOptions> options)
    {
        _appointmentStore = appointmentStore;
        _rescheduleStore = rescheduleStore;
        _outbox = outbox;
        _options = options;
    }

    public async Task<BookingRescheduleRequest> CreateRequestAsync(
        Guid requesterUserId,
        Guid appointmentId,
        DateTime newSlotStartUtc,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });

        if (reason.Length > 500)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason không được vượt quá 500 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "max 500 chars" } });

        var appt = await _appointmentStore.FindByIdAsync(appointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {appointmentId} not found.");

        if (appt.MemberId != requesterUserId && appt.PtUserId != requesterUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Not owner or PT.");

        if (await _appointmentStore.IsPackageAppointmentAsync(appointmentId, ct))
            throw new ConflictException("PACKAGE_ACTION_REQUIRED",
                "Buổi này thuộc một gói PT; không thể đổi lịch qua luồng lịch lẻ.");

        if (appt.Status != AppointmentStatus.Pending && appt.Status != AppointmentStatus.Confirmed)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot reschedule booking in status {appt.Status}.");

        var existingPending = await _rescheduleStore.FindPendingByAppointmentIdAsync(appointmentId, ct);
        if (existingPending != null)
            throw new ConflictException("RESCHEDULE_ALREADY_PENDING", "A reschedule request is already pending for this booking.");

        var opts = _options.Value;
        var nowUtc = DateTime.UtcNow;

        var oldSlotLeadTime = requesterUserId == appt.MemberId
            ? opts.MemberMinLeadTimeBeforeOldSlot
            : opts.MinLeadTimeBeforeOldSlot;
        if (appt.SlotStartUtc - nowUtc < oldSlotLeadTime)
            throw new ConflictException("MIN_LEAD_TIME_OLD_SLOT_VIOLATED", $"Old slot start time must be at least {oldSlotLeadTime.TotalHours} hours in the future.");

        if (newSlotStartUtc - nowUtc < opts.MinLeadTimeForNewSlot)
            throw new ConflictException("MIN_LEAD_TIME_NEW_SLOT_VIOLATED", $"New slot start time must be at least {opts.MinLeadTimeForNewSlot.TotalHours} hours in the future.");

        if (newSlotStartUtc > nowUtc.AddDays(opts.MaxAdvanceDays))
            throw new ConflictException("MAX_ADVANCE_DAYS_EXCEEDED", $"New slot cannot be scheduled more than {opts.MaxAdvanceDays} days in advance.");

        if (newSlotStartUtc == appt.SlotStartUtc)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "New slot start time must differ from current slot.",
                new Dictionary<string, string[]> { ["new_slot_start_utc"] = new[] { "must differ" } });

        var deadline1 = appt.SlotStartUtc - oldSlotLeadTime;
        var deadline2 = nowUtc.Add(opts.DefaultRequestTimeout);
        var deadlineUtc = deadline1 < deadline2 ? deadline1 : deadline2;

        if (await _appointmentStore.HasOverlappingSlotAsync(appt.PtUserId, newSlotStartUtc, appt.DurationMinutes, appt.Id, ct))
            throw new ConflictException(ErrorCodes.Booking.SlotTaken, $"Slot {newSlotStartUtc:O} of PT is no longer available.");

        var request = BookingRescheduleRequest.Create(
            appointmentId,
            requesterUserId,
            appt.SlotStartUtc,
            newSlotStartUtc,
            appt.DurationMinutes,
            reason.Trim(),
            deadlineUtc,
            nowUtc);

        await _rescheduleStore.AddAsync(request, ct);

        _outbox.Enqueue("booking.reschedule_requested", new
        {
            requestId = request.Id,
            appointmentId = appt.Id,
            requestedByUserId = requesterUserId,
            oldSlotStartUtc = request.OldSlotStartUtc,
            newSlotStartUtc = request.NewSlotStartUtc,
            deadlineUtc = request.DeadlineUtc
        });

        await _rescheduleStore.SaveChangesAsync(ct);
        return request;
    }

    public async Task<BookingRescheduleRequest> AcceptRequestAsync(
        Guid responderUserId,
        Guid requestId,
        CancellationToken ct)
    {
        var request = await _rescheduleStore.FindByIdAsync(requestId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Reschedule request {requestId} not found.");

        var appt = await _appointmentStore.FindByIdAsync(request.AppointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {request.AppointmentId} not found.");

        // Two-party consent authorization
        if (request.RequestedByUserId == appt.MemberId)
        {
            if (responderUserId == appt.MemberId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Cannot accept your own reschedule request.");
            if (responderUserId != appt.PtUserId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Only the PT can respond to this reschedule request.");
        }
        else if (request.RequestedByUserId == appt.PtUserId)
        {
            if (responderUserId == appt.PtUserId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Cannot accept your own reschedule request.");
            if (responderUserId != appt.MemberId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Only the Member can respond to this reschedule request.");
        }
        else
        {
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Unauthorized responder.");
        }

        ConflictException? conflictToThrow = null;

        await _appointmentStore.ExecuteWithScheduleLockAsync(appt.PtUserId, async () =>
        {
            var nowUtc = DateTime.UtcNow;

            if (nowUtc > request.DeadlineUtc)
            {
                request.Expire();
                _outbox.Enqueue("booking.reschedule_expired", new
                {
                    requestId = request.Id,
                    appointmentId = appt.Id,
                    expiredAtUtc = nowUtc
                });
                await _rescheduleStore.SaveChangesAsync(ct);
                conflictToThrow = new ConflictException("DEADLINE_EXPIRED", "Reschedule request response deadline has expired.");
                return request;
            }

            var isOverlapping = await _appointmentStore.HasOverlappingSlotAsync(
                appt.PtUserId, request.NewSlotStartUtc, request.DurationMinutes, appt.Id, ct);

            if (isOverlapping)
            {
                request.Reject("SLOT_NO_LONGER_AVAILABLE", nowUtc);
                _outbox.Enqueue("booking.reschedule_rejected", new
                {
                    requestId = request.Id,
                    appointmentId = appt.Id,
                    reason = "SLOT_NO_LONGER_AVAILABLE"
                });
                await _rescheduleStore.SaveChangesAsync(ct);
                conflictToThrow = new ConflictException("SLOT_NO_LONGER_AVAILABLE", "The requested slot is no longer available.");
                return request;
            }

            appt.RescheduleSlot(request.NewSlotStartUtc, request.NewSlotStartUtc, request.DurationMinutes);
            request.Accept(nowUtc);

            _outbox.Enqueue("booking.rescheduled", new
            {
                appointmentId = appt.Id,
                requestId = request.Id,
                oldSlotStartUtc = request.OldSlotStartUtc,
                newSlotStartUtc = request.NewSlotStartUtc,
                respondedByUserId = responderUserId
            });

            await _appointmentStore.SaveChangesAsync(ct);
            await _rescheduleStore.SaveChangesAsync(ct);
            return request;
        }, ct);

        if (conflictToThrow != null)
        {
            throw conflictToThrow;
        }

        return request;
    }

    public async Task<BookingRescheduleRequest> RejectRequestAsync(
        Guid responderUserId,
        Guid requestId,
        string? rejectionReason,
        CancellationToken ct)
    {
        var request = await _rescheduleStore.FindByIdAsync(requestId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Reschedule request {requestId} not found.");

        var appt = await _appointmentStore.FindByIdAsync(request.AppointmentId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Appointment {request.AppointmentId} not found.");

        // Two-party consent authorization
        if (request.RequestedByUserId == appt.MemberId)
        {
            if (responderUserId == appt.MemberId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Cannot reject your own reschedule request. Use Cancel instead.");
            if (responderUserId != appt.PtUserId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Only the PT can respond to this reschedule request.");
        }
        else if (request.RequestedByUserId == appt.PtUserId)
        {
            if (responderUserId == appt.PtUserId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Cannot reject your own reschedule request. Use Cancel instead.");
            if (responderUserId != appt.MemberId)
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Only the Member can respond to this reschedule request.");
        }
        else
        {
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "Unauthorized responder.");
        }

        var nowUtc = DateTime.UtcNow;
        request.Reject(rejectionReason, nowUtc);

        _outbox.Enqueue("booking.reschedule_rejected", new
        {
            requestId = request.Id,
            appointmentId = appt.Id,
            respondedByUserId = responderUserId,
            rejectionReason
        });

        await _rescheduleStore.SaveChangesAsync(ct);
        return request;
    }

    public async Task<BookingRescheduleRequest> CancelRequestAsync(
        Guid requesterUserId,
        Guid requestId,
        CancellationToken ct)
    {
        var request = await _rescheduleStore.FindByIdAsync(requestId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Reschedule request {requestId} not found.");

        if (request.RequestedByUserId != requesterUserId)
            throw new ForbiddenException(ErrorCodes.Booking.NotOwner, "Only the requester can cancel the reschedule request.");

        var nowUtc = DateTime.UtcNow;
        request.Cancel(nowUtc);

        _outbox.Enqueue("booking.reschedule_cancelled", new
        {
            requestId = request.Id,
            appointmentId = request.AppointmentId,
            cancelledByUserId = requesterUserId
        });

        await _rescheduleStore.SaveChangesAsync(ct);
        return request;
    }
}
