using System;
using Fitmate.Domain.Common;

namespace Fitmate.Domain.Booking;

public class BookingRescheduleRequest : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid AppointmentId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime OldSlotStartUtc { get; private set; }
    public DateTime NewSlotStartUtc { get; private set; }
    public int DurationMinutes { get; private set; }
    public RescheduleRequestStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTime DeadlineUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RespondedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }

    private BookingRescheduleRequest() { } // EF Core

    public static BookingRescheduleRequest Create(
        Guid appointmentId,
        Guid requestedByUserId,
        DateTime oldSlotStartUtc,
        DateTime newSlotStartUtc,
        int durationMinutes,
        string reason,
        DateTime deadlineUtc,
        DateTime? nowUtc = null)
    {
        var created = nowUtc ?? DateTime.UtcNow;
        return new BookingRescheduleRequest
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointmentId,
            RequestedByUserId = requestedByUserId,
            OldSlotStartUtc = oldSlotStartUtc,
            NewSlotStartUtc = newSlotStartUtc,
            DurationMinutes = durationMinutes,
            Status = RescheduleRequestStatus.Pending,
            Reason = reason,
            DeadlineUtc = deadlineUtc,
            CreatedAtUtc = created
        };
    }

    public void Accept(DateTime nowUtc)
    {
        if (Status != RescheduleRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot accept reschedule request in status {Status}.");
        if (nowUtc > DeadlineUtc)
            throw new ConflictException("DEADLINE_EXPIRED", "Reschedule request response deadline has expired.");
        Status = RescheduleRequestStatus.Accepted;
        RespondedAtUtc = nowUtc;
        Touch();
    }

    public void Reject(string? rejectionReason, DateTime nowUtc)
    {
        if (Status != RescheduleRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot reject reschedule request in status {Status}.");
        Status = RescheduleRequestStatus.Rejected;
        RejectionReason = rejectionReason;
        RespondedAtUtc = nowUtc;
        Touch();
    }

    public void Cancel(DateTime nowUtc)
    {
        if (Status != RescheduleRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot cancel reschedule request in status {Status}.");
        Status = RescheduleRequestStatus.Cancelled;
        RespondedAtUtc = nowUtc;
        Touch();
    }

    public void Expire()
    {
        if (Status == RescheduleRequestStatus.Pending)
        {
            Status = RescheduleRequestStatus.Expired;
            Touch();
        }
    }
}
