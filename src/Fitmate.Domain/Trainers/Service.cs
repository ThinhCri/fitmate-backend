using Fitmate.Domain.Common;

namespace Fitmate.Domain.Trainers;

public class Service : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid PtUserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public long PriceVnd { get; private set; }
    public long Package4PriceVnd { get; private set; }
    public long Package8PriceVnd { get; private set; }
    public int DurationMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public ServiceKind Kind { get; private set; } = ServiceKind.OneToOne;
    public ServiceDeliveryMode DeliveryMode { get; private set; } = ServiceDeliveryMode.InPerson;
    public string? TargetAudience { get; private set; }
    public string? RequiredEquipment { get; private set; }
    public string? Deliverable { get; private set; }
    public string? LocationName { get; private set; }
    public string? LocationAddress { get; private set; }

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

    public void SetPackagePrices(long? package4PriceVnd, long? package8PriceVnd)
    {
        var four = package4PriceVnd ?? Package4PriceVnd;
        var eight = package8PriceVnd ?? Package8PriceVnd;
        if (four < 0 || eight < 0 ||
            four > (decimal)PriceVnd * 4 || eight > (decimal)PriceVnd * 8)
            throw new ArgumentException("Package prices must be zero or no greater than buying the sessions separately.");
        Package4PriceVnd = four;
        Package8PriceVnd = eight;
        Touch();
    }

    public void ConfigureDetails(ServiceKind kind, ServiceDeliveryMode mode, string targetAudience,
        string requiredEquipment, string deliverable, string? locationName, string? locationAddress)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid service type or delivery mode.");
        if (string.IsNullOrWhiteSpace(targetAudience) || targetAudience.Length > 300 ||
            string.IsNullOrWhiteSpace(requiredEquipment) || requiredEquipment.Length > 300 ||
            string.IsNullOrWhiteSpace(deliverable) || deliverable.Length > 500)
            throw new ArgumentException("Audience, equipment and deliverable are required within their length limits.");
        if (mode == ServiceDeliveryMode.InPerson &&
            (string.IsNullOrWhiteSpace(locationName) || string.IsNullOrWhiteSpace(locationAddress) ||
             locationName.Length > 200 || locationAddress.Length > 500))
            throw new ArgumentException("In-person services require one location name and address.");
        Kind = kind;
        DeliveryMode = mode;
        TargetAudience = targetAudience.Trim();
        RequiredEquipment = requiredEquipment.Trim();
        Deliverable = deliverable.Trim();
        LocationName = mode == ServiceDeliveryMode.InPerson ? locationName!.Trim() : null;
        LocationAddress = mode == ServiceDeliveryMode.InPerson ? locationAddress!.Trim() : null;
        if (mode == ServiceDeliveryMode.Online) IsActive = false;
        Touch();
    }

}

public enum ServiceKind { OneToOne = 0, AssessmentAndPlan = 1 }
public enum ServiceDeliveryMode { InPerson = 0, Online = 1 }
