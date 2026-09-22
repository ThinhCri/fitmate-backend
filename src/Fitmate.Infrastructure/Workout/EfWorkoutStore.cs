using Fitmate.Application.Workout;
using Fitmate.Domain.Workout;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Workout;

public class EfWorkoutStore : IWorkoutStore
{
    private readonly FitmateDbContext _db;

    public EfWorkoutStore(FitmateDbContext db) => _db = db;

    public Task<WorkoutSession?> FindSessionByIdAsync(Guid sessionId, CancellationToken ct) =>
        _db.WorkoutSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.DeletedAt == null, ct);

    public Task<WorkoutSession?> FindSessionByClientIdAsync(Guid userId, string clientId, CancellationToken ct) =>
        _db.WorkoutSessions
            .FirstOrDefaultAsync(s =>
                s.UserId == userId && s.ClientId == clientId && s.DeletedAt == null, ct);

    public async Task<IReadOnlyList<WorkoutSession>> ListSessionsAsync(
        Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = _db.WorkoutSessions
            .Where(s => s.UserId == userId && s.DeletedAt == null);

        if (from.HasValue) q = q.Where(s => s.Date >= from.Value);
        if (to.HasValue) q = q.Where(s => s.Date <= to.Value);

        return await q
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<WorkoutSessionExercise?> FindSessionExerciseByIdAsync(Guid exerciseId, CancellationToken ct) =>
        _db.WorkoutSessionExercises
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.DeletedAt == null, ct);

    public async Task<IReadOnlyList<WorkoutSessionExercise>> ListExercisesAsync(Guid sessionId, CancellationToken ct) =>
        await _db.WorkoutSessionExercises
            .Where(e => e.WorkoutSessionId == sessionId && e.DeletedAt == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkoutSessionExerciseSet>> ListSetsAsync(Guid exerciseId, CancellationToken ct) =>
        await _db.WorkoutSessionExerciseSets
            .Where(s => s.WorkoutSessionExerciseId == exerciseId && s.DeletedAt == null)
            .ToListAsync(ct);

    public Task<WorkoutPlan?> FindPlanByIdAsync(Guid planId, CancellationToken ct) =>
        _db.WorkoutPlans.FirstOrDefaultAsync(p => p.Id == planId && p.DeletedAt == null, ct);

    public async Task<IReadOnlyList<WorkoutPlan>> ListPlansAsync(Guid userId, CancellationToken ct) =>
        await _db.WorkoutPlans
            .Where(p => p.UserId == userId && p.DeletedAt == null)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Exercise>> ListPlanExercisesAsync(Guid planId, CancellationToken ct) =>
        await _db.Exercises
            .Where(e => e.WorkoutPlanId == planId && e.DeletedAt == null)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, int>> CountPlanExercisesAsync(IReadOnlyList<Guid> planIds, CancellationToken ct) =>
        await _db.Exercises
            .Where(e => planIds.Contains(e.WorkoutPlanId) && e.DeletedAt == null)
            .GroupBy(e => e.WorkoutPlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count, ct);

    public Task<Exercise?> FindCatalogExerciseByIdAsync(Guid exerciseId, CancellationToken ct) =>
        _db.Exercises.FirstOrDefaultAsync(e => e.Id == exerciseId && e.DeletedAt == null, ct);

    public async Task AddPlanAsync(WorkoutPlan plan, CancellationToken ct) =>
        await _db.WorkoutPlans.AddAsync(plan, ct);

    public async Task AddPlanExerciseAsync(Exercise exercise, CancellationToken ct) =>
        await _db.Exercises.AddAsync(exercise, ct);

    public async Task DeletePlanExercisesAsync(Guid planId, CancellationToken ct)
    {
        var exercises = await _db.Exercises
            .Where(e => e.WorkoutPlanId == planId && e.DeletedAt == null)
            .ToListAsync(ct);
        foreach (var ex in exercises)
        {
            ex.SoftDelete();
        }
    }

    public Task DeletePlanAsync(WorkoutPlan plan, CancellationToken ct)
    {
        plan.SoftDelete();
        return Task.CompletedTask;
    }

    public async Task AddSessionAsync(WorkoutSession session, CancellationToken ct) =>
        await _db.WorkoutSessions.AddAsync(session, ct);

    public async Task AddExerciseAsync(WorkoutSessionExercise exercise, CancellationToken ct) =>
        await _db.WorkoutSessionExercises.AddAsync(exercise, ct);

    public async Task AddSetAsync(WorkoutSessionExerciseSet set, CancellationToken ct) =>
        await _db.WorkoutSessionExerciseSets.AddAsync(set, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
