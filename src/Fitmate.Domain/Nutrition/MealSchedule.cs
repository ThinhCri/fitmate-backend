using Fitmate.Domain.Common;

namespace Fitmate.Domain.Nutrition;

public class MealSchedule : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string? Breakfast { get; private set; }
    public string? Lunch { get; private set; }
    public string? Dinner { get; private set; }
    public string? Snack { get; private set; }

    private MealSchedule() { }
    public static MealSchedule Create(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Update(string? breakfast, string? lunch, string? dinner, string? snack)
    {
        Breakfast = breakfast;
        Lunch = lunch;
        Dinner = dinner;
        Snack = snack;
        Touch();
    }
}
