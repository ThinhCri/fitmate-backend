namespace Fitmate.Domain.Booking;

public class PtScheduleLock
{
    public Guid PtUserId { get; private set; }
    public DateTime LockedAtUtc { get; private set; }

    // Private constructor for EF Core
    private PtScheduleLock() { }

    public PtScheduleLock(Guid ptUserId, DateTime lockedAtUtc)
    {
        PtUserId = ptUserId;
        LockedAtUtc = lockedAtUtc;
    }

    public void Touch(DateTime atUtc)
    {
        LockedAtUtc = atUtc;
    }
}
