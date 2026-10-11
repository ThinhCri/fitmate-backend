using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using Fitmate.Application.Ai;

namespace Fitmate.Application.Nutrition;

public class NutritionService : INutritionService
{
    private readonly INutritionStore _store;
    private readonly IAiNutritionAdapter _ai;
    private readonly IOutboxDispatcher _outbox;
    private readonly IAiUsageService? _usage;

    public NutritionService(INutritionStore store, IAiNutritionAdapter ai, IOutboxDispatcher outbox, IAiUsageService? usage = null)
    {
        _store = store;
        _ai = ai;
        _outbox = outbox;
        _usage = usage;
    }

    public async Task<IReadOnlyList<MealLogView>> ListMealsAsync(
        Guid userId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var meals = await _store.ListMealsAsync(userId, from, to, ct);
        return meals.Select(MapMeal).ToList();
    }

    public async Task<MealLogView?> GetMealAsync(Guid userId, Guid mealId, CancellationToken ct = default)
    {
        var meal = await _store.FindMealByIdAsync(mealId, ct);
        return meal is null || meal.UserId != userId ? null : MapMeal(meal);
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
            confirmed: request.Confirmed,
            loggedAt: request.LoggedAt,
            mealSlot: request.MealSlot,
            servingQuantity: request.ServingQuantity,
            servingUnit: request.ServingUnit?.Trim(),
            grams: request.Grams,
            notes: request.Notes?.Trim());

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
            confirmed: request.Confirmed,
            mealSlot: request.MealSlot,
            servingQuantity: request.ServingQuantity,
            servingUnit: request.ServingUnit?.Trim(),
            grams: request.Grams,
            notes: request.Notes?.Trim());

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

    public async Task<IReadOnlyList<WeeklyMealPlanItemView>> ListWeeklyPlanAsync(
        Guid userId, DateOnly weekStart, CancellationToken ct = default)
    {
        ValidateWeekStart(weekStart);
        var items = await _store.ListWeeklyPlanItemsAsync(userId, weekStart, ct);
        var consumptions = await _store.ListWeeklyPlanConsumptionsAsync(userId, weekStart, weekStart.AddDays(7), ct);
        var byItem = consumptions.ToDictionary(x => x.PlanItemId);
        return items.Select(item => MapWeeklyPlanItem(item, byItem.GetValueOrDefault(item.Id))).ToList();
    }

    public async Task<WeeklyMealPlanItemView> CreateWeeklyPlanItemAsync(
        Guid userId, UpsertWeeklyMealPlanItemRequest request, DateOnly weekStart, CancellationToken ct = default)
    {
        ValidateWeekStart(weekStart);
        ValidateWeeklyPlanRequest(request);
        var item = WeeklyMealPlanItem.Create(userId, weekStart, request.DayOfWeek, request.MealSlot,
            request.FoodName.Trim(), request.ServingQuantity, request.ServingUnit?.Trim(), request.Grams,
            request.Calories, request.ProteinG, request.CarbsG, request.FatG, request.Notes?.Trim());
        await _store.AddWeeklyPlanItemAsync(item, ct);
        await _store.SaveChangesAsync(ct);
        _outbox.Enqueue("nutrition.weekly_plan_item_created", new { itemId = item.Id, userId });
        return MapWeeklyPlanItem(item, null);
    }

    public async Task<WeeklyMealPlanItemView?> UpdateWeeklyPlanItemAsync(
        Guid userId, Guid itemId, UpsertWeeklyMealPlanItemRequest request,
        DateOnly weekStart, CancellationToken ct = default)
    {
        ValidateWeekStart(weekStart);
        ValidateWeeklyPlanRequest(request);
        var item = await _store.FindWeeklyPlanItemAsync(itemId, ct);
        if (item is null || item.UserId != userId) return null;
        if (item.WeekStart != weekStart)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Invalid week start.", new Dictionary<string, string[]>
                {
                    ["week_start"] = new[] { "must match the plan item's week_start" }
                });
        item.Update(request.DayOfWeek, request.MealSlot, request.FoodName.Trim(),
            request.ServingQuantity, request.ServingUnit?.Trim(), request.Grams,
            request.Calories, request.ProteinG, request.CarbsG, request.FatG, request.Notes?.Trim());
        await _store.SaveChangesAsync(ct);
        var consumption = await _store.FindWeeklyPlanConsumptionAsync(
            item.Id, weekStart.AddDays(item.DayOfWeek), ct);
        _outbox.Enqueue("nutrition.weekly_plan_item_updated", new { itemId = item.Id, userId });
        return MapWeeklyPlanItem(item, consumption);
    }

    public async Task DeleteWeeklyPlanItemAsync(
        Guid userId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _store.FindWeeklyPlanItemAsync(itemId, ct);
        if (item is null || item.UserId != userId)
            throw new NotFoundException(NutritionErrorCodes.MealNotFound, "Planned meal not found.");
        await _store.DeleteWeeklyPlanItemAsync(item, ct);
        await _store.SaveChangesAsync(ct);
        _outbox.Enqueue("nutrition.weekly_plan_item_deleted", new { itemId = item.Id, userId });
    }

    public async Task<MealLogView?> ConsumeWeeklyPlanItemAsync(
        Guid userId, Guid itemId, DateOnly date, CancellationToken ct = default)
    {
        var item = await _store.FindWeeklyPlanItemAsync(itemId, ct);
        if (item is null || item.UserId != userId) return null;
        var expectedDate = item.WeekStart.AddDays(item.DayOfWeek);
        if (date != expectedDate)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Invalid consumption date.", new Dictionary<string, string[]>
                {
                    ["date"] = new[] { "must match week_start plus day_of_week" }
                });

        var existing = await _store.FindWeeklyPlanConsumptionAsync(item.Id, date, ct);
        if (existing is not null)
        {
            var existingMeal = await _store.FindMealByIdAsync(existing.MealLogId, ct);
            return existingMeal is null ? null : MapMeal(existingMeal);
        }

        var meal = MealLog.Create(userId, item.FoodName, null, item.Calories, item.ProteinG,
            item.CarbsG, item.FatG, item.ServingQuantity, MealSource.Manual, null, true,
            date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), item.MealSlot,
            item.ServingQuantity, item.ServingUnit, item.Grams, item.Notes);
        var consumption = WeeklyMealPlanConsumption.Create(userId, item.Id, meal.Id, date);
        if (!await _store.TryAddMealConsumptionAsync(meal, consumption, ct))
        {
            var winner = await _store.FindWeeklyPlanConsumptionAsync(item.Id, date, ct);
            if (winner is null) throw new DomainException(
                "FITMATE.NUTRITION.CONSUME_CONFLICT",
                "The planned meal could not be consumed. Please retry.", 409);
            var winnerMeal = await _store.FindMealByIdAsync(winner.MealLogId, ct);
            return winnerMeal is null ? null : MapMeal(winnerMeal);
        }
        _outbox.Enqueue("nutrition.weekly_plan_item_consumed", new
        {
            itemId = item.Id,
            mealId = meal.Id,
            userId,
            date
        });
        return MapMeal(meal);
    }

    public async Task<NutritionScanResultView> ScanMealAsync(
        Guid userId, Stream imageStream, string contentType, CancellationToken ct = default)
    {
        var reservation = _usage is null
            ? new AiUsageReservation(Guid.Empty, false)
            : await _usage.ReserveAsync(userId, "scan", ct);
        try
        {
            var result = await _ai.EstimateFromImageAsync(imageStream, contentType, ct);
            if (_usage is not null) await _usage.CommitAsync(reservation, ct);
            return result;
        }
        catch
        {
            if (_usage is not null) await _usage.ReleaseAsync(reservation, CancellationToken.None);
            throw;
        }
    }

    private static void ValidateWeekStart(DateOnly weekStart)
    {
        if (weekStart.DayOfWeek != DayOfWeek.Monday)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Invalid week start.", new Dictionary<string, string[]>
                {
                    ["week_start"] = new[] { "must be a Monday" }
                });
    }

    private static void ValidateWeeklyPlanRequest(UpsertWeeklyMealPlanItemRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.DayOfWeek is < 0 or > 6) errors["day_of_week"] = new[] { "must be 0..6 (Monday..Sunday)" };
        if (string.IsNullOrWhiteSpace(request.FoodName)) errors["food_name"] = new[] { "required" };
        else if (request.FoodName.Trim().Length > 200) errors["food_name"] = new[] { "max length 200 characters" };
        if (request.Calories is < 0 or > 10000) errors["calories"] = new[] { "must be 0..10000" };
        if (request.ProteinG is < 0 or > 1000) errors["protein_g"] = new[] { "must be 0..1000" };
        if (request.CarbsG is < 0 or > 1000) errors["carbs_g"] = new[] { "must be 0..1000" };
        if (request.FatG is < 0 or > 1000) errors["fat_g"] = new[] { "must be 0..1000" };
        if (request.ServingQuantity is <= 0 or > 10000) errors["serving_quantity"] = new[] { "must be > 0 and <= 10000" };
        if (request.Grams is <= 0 or > 100000) errors["grams"] = new[] { "must be > 0 and <= 100000" };
        if (request.ServingUnit?.Trim().Length > 50) errors["serving_unit"] = new[] { "max length 50 characters" };
        if (request.Notes?.Trim().Length > 1000) errors["notes"] = new[] { "max length 1000 characters" };
        if (errors.Count > 0)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Invalid weekly meal plan item.", errors);
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
        if (request.ServingQuantity.HasValue && (request.ServingQuantity.Value <= 0 || request.ServingQuantity.Value > 1000))
            errors["serving_quantity"] = new[] { "must be > 0 and <= 1000" };
        if (request.Grams.HasValue && (request.Grams.Value <= 0 || request.Grams.Value > 10000))
            errors["grams"] = new[] { "must be > 0 and <= 10000" };
        if (request.ServingUnit?.Length > 50) errors["serving_unit"] = new[] { "max length 50 characters" };
        if (request.Notes?.Length > 1000) errors["notes"] = new[] { "max length 1000 characters" };

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
        if (request.ServingQuantity.HasValue && (request.ServingQuantity.Value <= 0 || request.ServingQuantity.Value > 1000))
            errors["serving_quantity"] = new[] { "must be > 0 and <= 1000" };
        if (request.Grams.HasValue && (request.Grams.Value <= 0 || request.Grams.Value > 10000))
            errors["grams"] = new[] { "must be > 0 and <= 10000" };
        if (request.ServingUnit?.Length > 50) errors["serving_unit"] = new[] { "max length 50 characters" };
        if (request.Notes?.Length > 1000) errors["notes"] = new[] { "max length 1000 characters" };

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
        MealSlot: m.MealSlot,
        ServingQuantity: m.ServingQuantity,
        ServingUnit: m.ServingUnit,
        Grams: m.Grams,
        Notes: m.Notes,
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

    private static WeeklyMealPlanItemView MapWeeklyPlanItem(
        WeeklyMealPlanItem item, WeeklyMealPlanConsumption? consumption) => new(
        Id: item.Id,
        WeekStart: item.WeekStart,
        DayOfWeek: item.DayOfWeek,
        MealSlot: item.MealSlot,
        FoodName: item.FoodName,
        ServingQuantity: item.ServingQuantity,
        ServingUnit: item.ServingUnit,
        Grams: item.Grams,
        Calories: item.Calories,
        ProteinG: item.ProteinG,
        CarbsG: item.CarbsG,
        FatG: item.FatG,
        Notes: item.Notes,
        IsConsumed: consumption is not null,
        ConsumedDate: consumption?.ConsumedDate,
        MealLogId: consumption?.MealLogId);
}
