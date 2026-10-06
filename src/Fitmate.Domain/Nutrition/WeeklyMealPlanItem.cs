using Fitmate.Domain.Common;

namespace Fitmate.Domain.Nutrition;

public sealed class WeeklyMealPlanItem : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly WeekStart { get; private set; }
    public int DayOfWeek { get; private set; }
    public MealSlot MealSlot { get; private set; }
    public string FoodName { get; private set; } = null!;
    public decimal? ServingQuantity { get; private set; }
    public string? ServingUnit { get; private set; }
    public decimal? Grams { get; private set; }
    public int Calories { get; private set; }
    public int ProteinG { get; private set; }
    public int CarbsG { get; private set; }
    public int FatG { get; private set; }
    public string? Notes { get; private set; }

    private WeeklyMealPlanItem() { }

    public static WeeklyMealPlanItem Create(Guid userId, DateOnly weekStart, int dayOfWeek, MealSlot mealSlot,
        string foodName, decimal? servingQuantity, string? servingUnit, decimal? grams,
        int calories, int proteinG, int carbsG, int fatG, string? notes)
    {
        var item = new WeeklyMealPlanItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            WeekStart = weekStart,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        item.Update(dayOfWeek, mealSlot, foodName, servingQuantity, servingUnit, grams,
            calories, proteinG, carbsG, fatG, notes);
        return item;
    }

    public void Update(int dayOfWeek, MealSlot mealSlot, string foodName,
        decimal? servingQuantity, string? servingUnit, decimal? grams,
        int calories, int proteinG, int carbsG, int fatG, string? notes)
    {
        DayOfWeek = dayOfWeek;
        MealSlot = mealSlot;
        FoodName = foodName;
        ServingQuantity = servingQuantity;
        ServingUnit = servingUnit;
        Grams = grams;
        Calories = calories;
        ProteinG = proteinG;
        CarbsG = carbsG;
        FatG = fatG;
        Notes = notes;
        Touch();
    }
}
