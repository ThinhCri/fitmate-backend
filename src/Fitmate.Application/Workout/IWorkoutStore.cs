using Fitmate.Domain.Workout;

namespace Fitmate.Application.Workout;

public interface IWorkoutStore
{
    Task<WorkoutSession?> FindSessionByIdAsync(Guid sessionId, CancellationToken ct);
    Task<WorkoutSession?> FindSessionByClientIdAsync(Guid userId, string clientId, CancellationToken ct);
    Task<IReadOnlyList<WorkoutSession>> ListSessionsAsync(
        Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct);
    Task<WorkoutSessionExercise?> FindSessionExerciseByIdAsync(Guid exerciseId, CancellationToken ct);
    Task<IReadOnlyList<WorkoutSessionExercise>> ListExercisesAsync(Guid sessionId, CancellationToken ct);
    Task<IReadOnlyList<WorkoutSessionExerciseSet>> ListSetsAsync(Guid exerciseId, CancellationToken ct);
    Task<WorkoutPlan?> FindPlanByIdAsync(Guid planId, CancellationToken ct);
    Task<IReadOnlyList<WorkoutPlan>> ListPlansAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<Exercise>> ListPlanExercisesAsync(Guid planId, CancellationToken ct);
    Task<Dictionary<Guid, int>> CountPlanExercisesAsync(IReadOnlyList<Guid> planIds, CancellationToken ct);
    Task<Exercise?> FindCatalogExerciseByIdAsync(Guid exerciseId, CancellationToken ct);
    Task AddPlanAsync(WorkoutPlan plan, CancellationToken ct);
    Task AddPlanExerciseAsync(Exercise exercise, CancellationToken ct);
    Task DeletePlanExercisesAsync(Guid planId, CancellationToken ct);
    Task DeletePlanAsync(WorkoutPlan plan, CancellationToken ct);
    Task AddSessionAsync(WorkoutSession session, CancellationToken ct);
    Task AddExerciseAsync(WorkoutSessionExercise exercise, CancellationToken ct);
    Task AddSetAsync(WorkoutSessionExerciseSet set, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IWeightStore
{
    Task<IReadOnlyList<WeightLog>> ListAsync(Guid userId, CancellationToken ct);
    Task<WeightLog?> FindByIdAsync(Guid weightLogId, CancellationToken ct);
    Task AddAsync(WeightLog log, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
