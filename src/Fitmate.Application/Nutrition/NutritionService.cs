using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

public class NutritionService : INutritionService
{
    private readonly INutritionStore _store;
    private readonly IAiNutritionAdapter _ai;

    public NutritionService(INutritionStore store, IAiNutritionAdapter ai)
    {
        _store = store;
        _ai = ai;
    }

    public async Task<IReadOnlyList<MealLogView>> ListMealsAsync(
        Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var meals = await _store.ListMealsAsync(userId, from, to, ct);
        return meals.Select(MapMeal).ToList();
    }

    public async Task<MealLogView> LogMealAsync(
        Guid userId, LogMealRequest request, CancellationToken ct = default)
    {
        var meal = MealLog.Create(
            userId: userId,
            foodName: request.FoodName,
            imageStorageKey: null,
            calories: request.Calories,
            protein: request.ProteinG,
            carbs: request.CarbsG,
            fat: request.FatG,
            portionSize: request.PortionSize,
            source: request.Source,
            aiConfidence: request.AiConfidence,
            confirmed: false,
            loggedAt: request.LoggedAt);

        await _store.AddMealAsync(meal, ct);
        await _store.SaveChangesAsync(ct);
        return MapMeal(meal);
    }

    public async Task<MealLogView?> UpdateMealAsync(
        Guid userId, Guid mealId, UpdateMealRequest request, CancellationToken ct = default)
    {
        var meal = await _store.FindMealByIdAsync(mealId, ct);
        if (meal is null || meal.UserId != userId)
            return null;

        meal.Update(
            foodName: request.FoodName,
            calories: request.Calories,
            protein: request.ProteinG,
            carbs: request.CarbsG,
            fat: request.FatG,
            portionSize: request.PortionSize,
            confirmed: request.Confirmed);

        await _store.SaveChangesAsync(ct);
        return MapMeal(meal);
    }

    public async Task<MealScheduleView> GetScheduleAsync(
        Guid userId, CancellationToken ct = default)
    {
        var schedule = await _store.FindScheduleByUserAsync(userId, ct);
        return schedule is null
            ? new MealScheduleView(Guid.Empty, null, null, null, null)
            : MapSchedule(schedule);
    }

    public async Task<MealScheduleView> UpsertScheduleAsync(
        Guid userId, UpsertScheduleRequest request, CancellationToken ct = default)
    {
        var existing = await _store.FindScheduleByUserAsync(userId, ct);

        if (existing is not null)
        {
            existing.Update(
                breakfast: request.Breakfast,
                lunch: request.Lunch,
                dinner: request.Dinner,
                snack: request.Snack);
        }
        else
        {
            var created = MealSchedule.Create(userId);
            created.Update(
                breakfast: request.Breakfast,
                lunch: request.Lunch,
                dinner: request.Dinner,
                snack: request.Snack);
            await _store.AddScheduleAsync(created, ct);
        }

        await _store.SaveChangesAsync(ct);

        var updated = await _store.FindScheduleByUserAsync(userId, ct);
        return MapSchedule(updated!);
    }

    public async Task<NutritionScanResultView> ScanMealAsync(
        Guid userId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        return await _ai.EstimateFromImageAsync(imageStream, contentType, ct);
    }

    private static MealLogView MapMeal(MealLog m) => new(
        Id: m.Id,
        FoodName: m.FoodName,
        ImageUrl: m.ImageStorageKey,
        Calories: m.Calories,
        ProteinG: m.ProteinG,
        CarbsG: m.CarbsG,
        FatG: m.FatG,
        PortionSize: m.PortionSize,
        Source: m.Source,
        AiConfidence: m.AiConfidence,
        Confirmed: m.Confirmed,
        LoggedAt: m.LoggedAt,
        CreatedAt: m.CreatedAt);

    private static MealScheduleView MapSchedule(MealSchedule s) => new(
        Id: s.Id,
        Breakfast: s.Breakfast,
        Lunch: s.Lunch,
        Dinner: s.Dinner,
        Snack: s.Snack);
}
