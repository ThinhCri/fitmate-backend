using Fitmate.Domain.Common;

namespace Fitmate.Domain.Trainers;

public class AvailabilitySlot : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PtUserId { get; private set; }
    public DateTime SlotStartUtc { get; private set; }
    public DateTime SlotStartLocal { get; private set; }
    public int DurationMinutes { get; private set; }
    public AvailabilityStatus Status { get; private set; }

    private AvailabilitySlot() { }

    public static AvailabilitySlot Create(Guid ptUserId, DateTime slotStartLocal, string trainerTimezone, int durationMinutes)
    {
        var local = DateTime.SpecifyKind(slotStartLocal, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, ResolveTimezone(trainerTimezone));
        return new AvailabilitySlot
        {
            Id = Guid.NewGuid(),
            PtUserId = ptUserId,
            SlotStartLocal = local,
            SlotStartUtc = utc,
            DurationMinutes = durationMinutes,
            Status = AvailabilityStatus.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void MarkHeld() { Status = AvailabilityStatus.Held; Touch(); }
    public void MarkBooked() { Status = AvailabilityStatus.Booked; Touch(); }
    public void ReleaseToOpen() { Status = AvailabilityStatus.Open; Touch(); }

    private static TimeZoneInfo ResolveTimezone(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
    }
}
