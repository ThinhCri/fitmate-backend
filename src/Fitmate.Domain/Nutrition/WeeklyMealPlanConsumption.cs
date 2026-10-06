using Fitmate.Domain.Common;

namespace Fitmate.Domain.Nutrition;

public sealed class WeeklyMealPlanConsumption : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid PlanItemId { get; private set; }
    public Guid MealLogId { get; private set; }
    public DateOnly ConsumedDate { get; private set; }

    private WeeklyMealPlanConsumption() { }

    public static WeeklyMealPlanConsumption Create(
        Guid userId, Guid planItemId, Guid mealLogId, DateOnly consumedDate) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        PlanItemId = planItemId,
        MealLogId = mealLogId,
        ConsumedDate = consumedDate,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
