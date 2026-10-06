using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

public interface INutritionStore
{
    Task<MealLog?> FindMealByIdAsync(Guid mealId, CancellationToken ct);
    Task<IReadOnlyList<MealLog>> ListMealsAsync(
        Guid userId, DateTime? from, DateTime? to, CancellationToken ct);
    Task AddMealAsync(MealLog meal, CancellationToken ct);
    Task DeleteMealAsync(MealLog meal, CancellationToken ct);
    Task<MealSchedule?> FindScheduleByUserAsync(Guid userId, CancellationToken ct);
    Task AddScheduleAsync(MealSchedule schedule, CancellationToken ct);
    Task<IReadOnlyList<WeeklyMealPlanItem>> ListWeeklyPlanItemsAsync(
        Guid userId, DateOnly weekStart, CancellationToken ct);
    Task<WeeklyMealPlanItem?> FindWeeklyPlanItemAsync(Guid itemId, CancellationToken ct);
    Task AddWeeklyPlanItemAsync(WeeklyMealPlanItem item, CancellationToken ct);
    Task DeleteWeeklyPlanItemAsync(WeeklyMealPlanItem item, CancellationToken ct);
    Task<IReadOnlyList<WeeklyMealPlanConsumption>> ListWeeklyPlanConsumptionsAsync(
        Guid userId, DateOnly from, DateOnly toExclusive, CancellationToken ct);
    Task<WeeklyMealPlanConsumption?> FindWeeklyPlanConsumptionAsync(
        Guid planItemId, DateOnly date, CancellationToken ct);
    Task AddWeeklyPlanConsumptionAsync(WeeklyMealPlanConsumption consumption, CancellationToken ct);
    Task<bool> TryAddMealConsumptionAsync(
        MealLog meal, WeeklyMealPlanConsumption consumption, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
