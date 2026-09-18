using Fitmate.Domain.Common;

namespace Fitmate.Domain.Trainers;

public class Service : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PtUserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public long PriceVnd { get; private set; }
    public int DurationMinutes { get; private set; }
    public bool IsActive { get; private set; }

    private Service() { }

    public static Service Create(Guid ptUserId, string name, string? description, long priceVnd, int durationMinutes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name required");
        if (priceVnd < 0) throw new ArgumentException("priceVnd must be >= 0");
        if (durationMinutes < 15 || durationMinutes > 240) throw new ArgumentException("durationMinutes 15..240");

        return new Service
        {
            Id = Guid.NewGuid(),
            PtUserId = ptUserId,
            Name = name,
            Description = description,
            PriceVnd = priceVnd,
            DurationMinutes = durationMinutes,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Update(string? name, string? description, long? priceVnd, int? durationMinutes, bool? isActive)
    {
        if (name != null) Name = name;
        if (description != null) Description = description;
        if (priceVnd.HasValue)
        {
            if (priceVnd.Value < 0) throw new ArgumentException("priceVnd must be >= 0");
            PriceVnd = priceVnd.Value;
        }
        if (durationMinutes.HasValue)
        {
            if (durationMinutes.Value < 15 || durationMinutes.Value > 240)
                throw new ArgumentException("durationMinutes 15..240");
            DurationMinutes = durationMinutes.Value;
        }
        if (isActive.HasValue) IsActive = isActive.Value;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }
}
