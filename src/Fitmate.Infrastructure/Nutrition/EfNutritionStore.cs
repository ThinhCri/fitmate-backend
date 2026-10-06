using Fitmate.Application.Nutrition;
using Fitmate.Domain.Nutrition;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Nutrition;

public class EfNutritionStore : INutritionStore
{
    private readonly FitmateDbContext _db;

    public EfNutritionStore(FitmateDbContext db) => _db = db;

    public Task<MealLog?> FindMealByIdAsync(Guid mealId, CancellationToken ct) =>
        _db.MealLogs.FirstOrDefaultAsync(m => m.Id == mealId && m.DeletedAt == null, ct);

    public async Task<IReadOnlyList<MealLog>> ListMealsAsync(
        Guid userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = _db.MealLogs.Where(m => m.UserId == userId && m.DeletedAt == null);

        if (from.HasValue) q = q.Where(m => m.LoggedAt >= from.Value);
        if (to.HasValue) q = q.Where(m => m.LoggedAt < to.Value);

        return await q
            .OrderByDescending(m => m.LoggedAt)
            .ThenByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    public Task AddMealAsync(MealLog meal, CancellationToken ct) =>
        Task.FromResult(_db.MealLogs.Add(meal));

    public Task DeleteMealAsync(MealLog meal, CancellationToken ct)
    {
        meal.SoftDelete();
        return Task.CompletedTask;
    }

    public Task<MealSchedule?> FindScheduleByUserAsync(Guid userId, CancellationToken ct) =>
        _db.MealSchedules.FirstOrDefaultAsync(s => s.UserId == userId && s.DeletedAt == null, ct);

    public Task AddScheduleAsync(MealSchedule schedule, CancellationToken ct) =>
        Task.FromResult(_db.MealSchedules.Add(schedule));

    public async Task<IReadOnlyList<WeeklyMealPlanItem>> ListWeeklyPlanItemsAsync(
        Guid userId, DateOnly weekStart, CancellationToken ct) => await _db.WeeklyMealPlanItems
        .Where(x => x.UserId == userId && x.WeekStart == weekStart && x.DeletedAt == null)
        .OrderBy(x => x.DayOfWeek).ThenBy(x => x.MealSlot).ThenBy(x => x.CreatedAt)
        .ToListAsync(ct);

    public Task<WeeklyMealPlanItem?> FindWeeklyPlanItemAsync(Guid itemId, CancellationToken ct) =>
        _db.WeeklyMealPlanItems.FirstOrDefaultAsync(x => x.Id == itemId && x.DeletedAt == null, ct);

    public Task AddWeeklyPlanItemAsync(WeeklyMealPlanItem item, CancellationToken ct) =>
        Task.FromResult(_db.WeeklyMealPlanItems.Add(item));

    public Task DeleteWeeklyPlanItemAsync(WeeklyMealPlanItem item, CancellationToken ct)
    {
        item.SoftDelete();
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<WeeklyMealPlanConsumption>> ListWeeklyPlanConsumptionsAsync(
        Guid userId, DateOnly from, DateOnly toExclusive, CancellationToken ct) =>
        await _db.WeeklyMealPlanConsumptions
            .Where(x => x.UserId == userId && x.ConsumedDate >= from && x.ConsumedDate < toExclusive && x.DeletedAt == null)
            .ToListAsync(ct);

    public Task<WeeklyMealPlanConsumption?> FindWeeklyPlanConsumptionAsync(
        Guid planItemId, DateOnly date, CancellationToken ct) =>
        _db.WeeklyMealPlanConsumptions.FirstOrDefaultAsync(
            x => x.PlanItemId == planItemId && x.ConsumedDate == date && x.DeletedAt == null, ct);

    public Task AddWeeklyPlanConsumptionAsync(WeeklyMealPlanConsumption consumption, CancellationToken ct) =>
        Task.FromResult(_db.WeeklyMealPlanConsumptions.Add(consumption));

    public async Task<bool> TryAddMealConsumptionAsync(
        MealLog meal, WeeklyMealPlanConsumption consumption, CancellationToken ct)
    {
        _db.MealLogs.Add(meal);
        _db.WeeklyMealPlanConsumptions.Add(consumption);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(meal).State = EntityState.Detached;
            _db.Entry(consumption).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
