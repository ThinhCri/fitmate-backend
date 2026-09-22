using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

public class NutritionService : INutritionService
{
    private readonly INutritionStore _store;
    private readonly IAiNutritionAdapter _ai;
    private readonly IOutboxDispatcher _outbox;

    public NutritionService(INutritionStore store, IAiNutritionAdapter ai, IOutboxDispatcher outbox)
    {
        _store = store;
        _ai = ai;
        _outbox = outbox;
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
        ValidateLogMealRequest(request);

        var meal = MealLog.Create(
            userId: userId,
            foodName: request.FoodName.Trim(),
            imageStorageKey: request.ImageUrl,
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

        _outbox.Enqueue("nutrition.meal_logged", new
        {
            mealId = meal.Id,
            userId,
            calories = meal.Calories,
            loggedAt = meal.LoggedAt.ToString("O")
        });

        return MapMeal(meal);
    }

    public async Task<MealLogView?> UpdateMealAsync(
        Guid userId, Guid mealId, UpdateMealRequest request, CancellationToken ct = default)
    {
        ValidateUpdateMealRequest(request);

        var meal = await _store.FindMealByIdAsync(mealId, ct);
        if (meal is null || meal.UserId != userId)
            return null;

        meal.Update(
            foodName: request.FoodName?.Trim(),
            calories: request.Calories,
            protein: request.ProteinG,
            carbs: request.CarbsG,
            fat: request.FatG,
            portionSize: request.PortionSize,
            confirmed: request.Confirmed);

        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("nutrition.meal_updated", new
        {
            mealId = meal.Id,
            userId,
            calories = meal.Calories
        });

        return MapMeal(meal);
    }

    public async Task DeleteMealAsync(Guid userId, Guid mealId, CancellationToken ct = default)
    {
        var meal = await _store.FindMealByIdAsync(mealId, ct);
        if (meal is null || meal.UserId != userId)
            throw new NotFoundException(NutritionErrorCodes.MealNotFound, $"Meal {mealId} not found.");

        await _store.DeleteMealAsync(meal, ct);
        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("nutrition.meal_deleted", new
        {
            mealId = meal.Id,
            userId
        });
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

    private static void ValidateLogMealRequest(LogMealRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FoodName))
            errors["food_name"] = new[] { "required" };
        else if (request.FoodName.Trim().Length > 200)
            errors["food_name"] = new[] { "max length 200 characters" };

        if (request.Calories < 0 || request.Calories > 10000)
            errors["calories"] = new[] { "must be 0..10000" };
        if (request.ProteinG < 0 || request.ProteinG > 1000)
            errors["protein_g"] = new[] { "must be 0..1000" };
        if (request.CarbsG < 0 || request.CarbsG > 1000)
            errors["carbs_g"] = new[] { "must be 0..1000" };
        if (request.FatG < 0 || request.FatG > 1000)
            errors["fat_g"] = new[] { "must be 0..1000" };

        if (request.PortionSize.HasValue && (request.PortionSize.Value <= 0 || request.PortionSize.Value > 10000))
            errors["portion_size"] = new[] { "must be > 0 and <= 10000" };

        if (request.LoggedAt.HasValue && request.LoggedAt.Value > DateTime.UtcNow.AddDays(1))
            errors["logged_at"] = new[] { "cannot be more than 1 day in the future" };

        if (errors.Count > 0)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Invalid meal request.",
                errors);
    }

    private static void ValidateUpdateMealRequest(UpdateMealRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.FoodName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.FoodName))
                errors["food_name"] = new[] { "cannot be empty" };
            else if (request.FoodName.Trim().Length > 200)
                errors["food_name"] = new[] { "max length 200 characters" };
        }

        if (request.Calories.HasValue && (request.Calories.Value < 0 || request.Calories.Value > 10000))
            errors["calories"] = new[] { "must be 0..10000" };
        if (request.ProteinG.HasValue && (request.ProteinG.Value < 0 || request.ProteinG.Value > 1000))
            errors["protein_g"] = new[] { "must be 0..1000" };
        if (request.CarbsG.HasValue && (request.CarbsG.Value < 0 || request.CarbsG.Value > 1000))
            errors["carbs_g"] = new[] { "must be 0..1000" };
        if (request.FatG.HasValue && (request.FatG.Value < 0 || request.FatG.Value > 1000))
            errors["fat_g"] = new[] { "must be 0..1000" };

        if (request.PortionSize.HasValue && (request.PortionSize.Value <= 0 || request.PortionSize.Value > 10000))
            errors["portion_size"] = new[] { "must be > 0 and <= 10000" };

        if (errors.Count > 0)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Invalid update meal request.",
                errors);
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
