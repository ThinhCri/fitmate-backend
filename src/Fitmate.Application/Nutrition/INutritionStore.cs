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
    Task SaveChangesAsync(CancellationToken ct);
}
