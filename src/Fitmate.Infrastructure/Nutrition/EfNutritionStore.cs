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
        _db.MealLogs.FirstOrDefaultAsync(m => m.Id == mealId, ct);

    public async Task<IReadOnlyList<MealLog>> ListMealsAsync(
        Guid userId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = _db.MealLogs.Where(m => m.UserId == userId);

        if (from.HasValue) q = q.Where(m => m.LoggedAt >= from.Value);
        if (to.HasValue) q = q.Where(m => m.LoggedAt <= to.Value);

        return await q
            .OrderByDescending(m => m.LoggedAt)
            .ThenByDescending(m => m.CreatedAt)
            .ToListAsync(ct);
    }

    public Task AddMealAsync(MealLog meal, CancellationToken ct) =>
        Task.FromResult(_db.MealLogs.Add(meal));

    public Task<MealSchedule?> FindScheduleByUserAsync(Guid userId, CancellationToken ct) =>
        _db.MealSchedules.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public Task AddScheduleAsync(MealSchedule schedule, CancellationToken ct) =>
        Task.FromResult(_db.MealSchedules.Add(schedule));

    public Task SaveChangesAsync(CancellationToken ct) =>
        _db.SaveChangesAsync(ct);
}
