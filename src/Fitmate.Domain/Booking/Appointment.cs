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
    public DateTime? CompletionSubmittedAtUtc { get; private set; }
    public DateTime? MemberConfirmationDeadlineUtc { get; private set; }
    public DateTime? DisputedAtUtc { get; private set; }
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
        var ttl = holdTtl ?? TimeSpan.FromMinutes(10);
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
        if (Status is not (AppointmentStatus.InProgress or AppointmentStatus.Confirmed))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot complete booking in status {Status}.");
        if (DateTime.UtcNow < SlotStartUtc.AddMinutes(DurationMinutes))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Cannot complete appointment before slot end.");
        var submittedAt = completedAtUtc ?? DateTime.UtcNow;
        Status = AppointmentStatus.AwaitingMemberConfirmation;
        CompletionSubmittedAtUtc = submittedAt;
        MemberConfirmationDeadlineUtc = submittedAt.AddHours(24);
        Touch();
    }

    public void ConfirmCompletionByMember(DateTime? confirmedAtUtc = null)
    {
        var now = confirmedAtUtc ?? DateTime.UtcNow;
        if (Status is AppointmentStatus.Confirmed or AppointmentStatus.InProgress)
        {
            if (DateTime.UtcNow < SlotStartUtc.AddMinutes(DurationMinutes))
                throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot confirm completion before slot end in status {Status}.");
            CompletionSubmittedAtUtc ??= now;
        }
        else if (Status != AppointmentStatus.AwaitingMemberConfirmation)
        {
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot confirm completion in status {Status}.");
        }
        Status = AppointmentStatus.Completed;
        CompletedAtUtc = now;
        LegacyCompletionTimeUnknown = false;
        Touch();
    }

    public void AutoConfirmCompletion(DateTime nowUtc)
    {
        if (Status != AppointmentStatus.AwaitingMemberConfirmation ||
            !MemberConfirmationDeadlineUtc.HasValue ||
            MemberConfirmationDeadlineUtc.Value > nowUtc)
            return;
        ConfirmCompletionByMember(nowUtc);
    }

    public void DisputeCompletion(string reason, DateTime? disputedAtUtc = null)
    {
        if (Status != AppointmentStatus.AwaitingMemberConfirmation)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, $"Cannot dispute completion in status {Status}.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Dispute reason must have at least 5 characters.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });
        Status = AppointmentStatus.Disputed;
        DisputedAtUtc = disputedAtUtc ?? DateTime.UtcNow;
        Note = reason.Trim();
        Touch();
    }

    public void ResolveCompletedByAdmin(DateTime nowUtc)
    {
        if (Status is not (AppointmentStatus.Confirmed or AppointmentStatus.InProgress or
            AppointmentStatus.AwaitingMemberConfirmation or AppointmentStatus.Disputed) ||
            nowUtc.Kind != DateTimeKind.Utc ||
            nowUtc < SlotStartUtc.AddMinutes(DurationMinutes))
            throw new ConflictException(ErrorCodes.Booking.CannotTransition,
                "The appointment is not eligible for this Admin attendance decision.");
        Status = AppointmentStatus.Completed;
        CompletedAtUtc = nowUtc;
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

    // Helper for testing legacy mappings and test fixtures
    public void ForceStatusForTesting(AppointmentStatus status, DateTime? completedAtUtc = null)
    {
        Status = status;
        if (status is AppointmentStatus.Confirmed or AppointmentStatus.InProgress or AppointmentStatus.Completed)
            ConfirmedAtUtc ??= SlotStartUtc.AddHours(-1);
        if (status is AppointmentStatus.InProgress or AppointmentStatus.Completed)
            StartedAtUtc ??= SlotStartUtc;
        if (status is AppointmentStatus.Completed && !LegacyCompletionTimeUnknown)
        {
            CompletedAtUtc ??= completedAtUtc ?? (DateTime.UtcNow > SlotStartUtc.AddMinutes(DurationMinutes) ? SlotStartUtc.AddMinutes(DurationMinutes) : DateTime.UtcNow);
        }
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
