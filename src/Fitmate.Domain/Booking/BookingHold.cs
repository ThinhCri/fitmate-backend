using Fitmate.Domain.Common;

namespace Fitmate.Domain.Booking;

/// <summary>
/// Slot giữ tạm, TTL 5 phút. Cron job release slot hết hạn.
/// </summary>
public class BookingHold : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid MemberId { get; private set; }
    public Guid PtUserId { get; private set; }
    public Guid ServiceId { get; private set; }
    public DateTime SlotStartUtc { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string Status { get; private set; } = "active"; // active | released | consumed

    private BookingHold() { }

    public static BookingHold Create(Guid memberId, Guid ptUserId, Guid serviceId, DateTime slotStartUtc, TimeSpan ttl) => new()
    {
        Id = Guid.NewGuid(),
        MemberId = memberId,
        PtUserId = ptUserId,
        ServiceId = serviceId,
        SlotStartUtc = slotStartUtc,
        ExpiresAt = DateTime.UtcNow.Add(ttl),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public bool IsActive => Status == "active" && DateTime.UtcNow < ExpiresAt;

    public void Release()
    {
        if (Status == "active")
        {
            Status = "released";
            Touch();
        }
    }

    public void Consume()
    {
        Status = "consumed";
        Touch();
    }
}
