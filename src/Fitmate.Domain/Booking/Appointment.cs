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
    public AppointmentStatus Status { get; private set; }
    public int? LegacyStatus { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public bool LegacyCompletionTimeUnknown { get; private set; }
    public DateTime? HoldExpiresAtUtc { get; private set; }
    public DateTime? AcceptanceDeadlineUtc { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public int RescheduledCount { get; private set; }
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
        long priceListedVnd, TimeSpan? holdTtl = null)
    {
        var ttl = holdTtl ?? TimeSpan.FromMinutes(5);
        return new Appointment
        {
            Id = Guid.NewGuid(),
            MemberId = memberId,
            PtUserId = ptUserId,
            ServiceId = serviceId,
            SlotStartUtc = slotStartUtc,
            SlotStartLocal = slotStartLocal,
            DurationMinutes = durationMinutes,
            Status = AppointmentStatus.Held,
            HoldExpiresAtUtc = DateTime.UtcNow.Add(ttl),
            PriceListedVnd = priceListedVnd,
            PaymentId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void AttachPayment(Guid paymentId, long actualPaidVnd, long discountVnd,
        decimal platformFeeRate, long platformFeeVnd, long trainerShareVnd, string policyVersion, DateTime? paidAtUtc = null)
    {
        PaymentId = paymentId;
        ActualPaidVnd = actualPaidVnd;
        DiscountVnd = discountVnd;
        PlatformFeeRate = platformFeeRate;
        PlatformFeeVnd = platformFeeVnd;
        TrainerShareVnd = trainerShareVnd;
        PolicyVersion = policyVersion;
        Status = AppointmentStatus.Pending;
        var paidAt = paidAtUtc ?? DateTime.UtcNow;
        var deadline = paidAt.AddHours(24);
        AcceptanceDeadlineUtc = deadline < SlotStartUtc ? deadline : SlotStartUtc;
        Touch();
    }

    public void ConfirmByPt()
    {
        if (Status != AppointmentStatus.Pending)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot confirm booking in status {Status}.");
        if (AcceptanceDeadlineUtc.HasValue && DateTime.UtcNow > AcceptanceDeadlineUtc.Value)
            throw new ConflictException("ACCEPTANCE_DEADLINE_EXPIRED", "Acceptance deadline has expired.");
        Status = AppointmentStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        Touch();
    }

    public void StartByPt()
    {
        if (Status != AppointmentStatus.Confirmed)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot start booking in status {Status}.");
        if (DateTime.UtcNow < SlotStartUtc.AddMinutes(-5))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Cannot start appointment before 5 minutes prior to slot time.");
        if (DateTime.UtcNow >= SlotStartUtc.AddMinutes(DurationMinutes))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Cannot start appointment after slot has already ended.");
        Status = AppointmentStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
        Touch();
    }

    public void CompleteByPt(DateTime? completedAtUtc = null)
    {
        if (Status != AppointmentStatus.InProgress)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot complete booking in status {Status}.");
        if (DateTime.UtcNow < SlotStartUtc.AddMinutes(DurationMinutes))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Cannot complete appointment before slot end.");
        Status = AppointmentStatus.Completed;
        CompletedAtUtc = completedAtUtc ?? DateTime.UtcNow;
        LegacyCompletionTimeUnknown = false;
        Touch();
    }

    public void RescheduleSlot(DateTime newSlotStartUtc, DateTime newSlotStartLocal, int newDurationMinutes)
    {
        SlotStartUtc = newSlotStartUtc;
        SlotStartLocal = newSlotStartLocal;
        DurationMinutes = newDurationMinutes;
        RescheduledCount++;
        Touch();
    }

    public void Cancel(string reason)
    {
        if (Status == AppointmentStatus.Completed)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot cancel in status {Status}.");
        Status = AppointmentStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        Note = reason;
        Touch();
    }

    public void ExpireHold()
    {
        if (Status == AppointmentStatus.Held)
        {
            Status = AppointmentStatus.Expired;
            Touch();
        }
    }

    public void ExpireAcceptance()
    {
        if (Status == AppointmentStatus.Pending)
        {
            Status = AppointmentStatus.Expired;
            Touch();
        }
    }

    public void ReconcileLegacyCompletion(DateTime verifiedCompletedAtUtc)
    {
        if (!LegacyCompletionTimeUnknown && CompletedAtUtc.HasValue)
            throw new ConflictException("TIMESTAMP_ALREADY_VERIFIED", "Completion timestamp has already been verified.");
        CompletedAtUtc = verifiedCompletedAtUtc;
        LegacyCompletionTimeUnknown = false;
        Touch();
    }

    public void SetLegacyData(int legacyStatus, DateTime? completedAtUtc, bool legacyCompletionTimeUnknown)
    {
        LegacyStatus = legacyStatus;
        CompletedAtUtc = completedAtUtc;
        LegacyCompletionTimeUnknown = legacyCompletionTimeUnknown;
    }

    // Helper for testing legacy mappings
    public void ForceStatusForTesting(AppointmentStatus status)
    {
        Status = status;
        Touch();
    }

    public static AppointmentStatus MapLegacyToAppointmentStatus(BookingStatus legacy) => legacy switch
    {
        BookingStatus.Held => AppointmentStatus.Held,
        BookingStatus.AwaitingPayment => AppointmentStatus.Held,
        BookingStatus.Paid => AppointmentStatus.Pending,
        BookingStatus.Confirmed => AppointmentStatus.Confirmed,
        BookingStatus.InProgress => AppointmentStatus.InProgress,
        BookingStatus.Completed => AppointmentStatus.Completed,
        BookingStatus.Cancelled => AppointmentStatus.Cancelled,
        BookingStatus.Refunded => AppointmentStatus.Cancelled,
        BookingStatus.Expired => AppointmentStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(legacy), legacy, null)
    };
}
