using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

public class WeightLog : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public decimal WeightKg { get; private set; }
    public DateTime RecordedAt { get; private set; }

    private WeightLog() { }
    public static WeightLog Create(Guid userId, decimal weightKg, DateTime? recordedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        WeightKg = weightKg,
        RecordedAt = recordedAt ?? DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
