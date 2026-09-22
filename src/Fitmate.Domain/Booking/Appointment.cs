using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;

namespace Fitmate.Domain.Booking;

public class Appointment : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid PtUserId { get; private set; }
    public Guid ServiceId { get; private set; }
    public DateTime SlotStartUtc { get; private set; }
    public DateTime SlotStartLocal { get; private set; }
    public int DurationMinutes { get; private set; }
    public BookingStatus Status { get; private set; }
    public long PriceListedVnd { get; private set; }
    public long DiscountVnd { get; private set; }
    public long ActualPaidVnd { get; private set; }
    public decimal PlatformFeeRate { get; private set; }
    public long PlatformFeeVnd { get; private set; }
    public long TrainerShareVnd { get; private set; }
    public string PolicyVersion { get; private set; } = "v1";
    public Guid? PaymentId { get; private set; }
    public string? Note { get; private set; }

    private Appointment() { }

    public static Appointment CreateHold(Guid memberId, Guid ptUserId, Guid serviceId,
        DateTime slotStartUtc, DateTime slotStartLocal, int durationMinutes,
        long priceListedVnd)
    {
        return new Appointment
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            PtUserId = ptUserId,
            ServiceId = serviceId,
            SlotStartUtc = slotStartUtc,
            SlotStartLocal = slotStartLocal,
            DurationMinutes = durationMinutes,
            Status = BookingStatus.Held,
            PriceListedVnd = priceListedVnd,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void AttachPayment(Guid paymentId, long actualPaidVnd, long discountVnd,
        decimal platformFeeRate, long platformFeeVnd, long trainerShareVnd, string policyVersion)
    {
        PaymentId = paymentId;
        ActualPaidVnd = actualPaidVnd;
        DiscountVnd = discountVnd;
        PlatformFeeRate = platformFeeRate;
        PlatformFeeVnd = platformFeeVnd;
        TrainerShareVnd = trainerShareVnd;
        PolicyVersion = policyVersion;
        Status = BookingStatus.Paid;
        Touch();
    }

    public void ConfirmByPt()
    {
        if (Status is not (BookingStatus.Paid or BookingStatus.AwaitingPayment))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot confirm booking in status {Status}.");
        Status = BookingStatus.Confirmed;
        Touch();
    }

    public void StartByPt()
    {
        if (Status is not (BookingStatus.Confirmed or BookingStatus.Paid))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot start booking in status {Status}.");
        if (DateTime.UtcNow < SlotStartUtc.AddMinutes(-5))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Cannot start appointment before 5 minutes prior to slot time.");
        Status = BookingStatus.InProgress;
        Touch();
    }

    public void CompleteByPt()
    {
        if (Status is not (BookingStatus.InProgress or BookingStatus.Confirmed or BookingStatus.Paid))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot complete booking in status {Status}.");
        Status = BookingStatus.Completed;
        Touch();
    }

    public void Reschedule(DateTime newSlotStartUtc, DateTime newSlotStartLocal, int newDurationMinutes)
    {
        if (Status is not (BookingStatus.Held or BookingStatus.Paid or BookingStatus.Confirmed))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot reschedule booking in status {Status}.");
        SlotStartUtc = newSlotStartUtc;
        SlotStartLocal = newSlotStartLocal;
        DurationMinutes = newDurationMinutes;
        Touch();
    }

    public void Cancel(string reason)
    {
        if (Status is BookingStatus.Completed or BookingStatus.Refunded or BookingStatus.Cancelled)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot cancel in status {Status}.");
        Status = BookingStatus.Cancelled;
        Note = reason;
        Touch();
    }

    public void MarkRefunded()
    {
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded or BookingStatus.Held)
            Status = BookingStatus.Refunded;
        else if (Status == BookingStatus.Paid)
            Status = BookingStatus.Refunded;
        else
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot refund in status {Status}.");
        Touch();
    }

    public void MarkExpired()
    {
        if (Status == BookingStatus.Held)
        {
            Status = BookingStatus.Expired;
            Touch();
        }
    }
}
