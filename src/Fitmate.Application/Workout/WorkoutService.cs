using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Auth;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;

namespace Fitmate.Application.Workout;

/// <summary>
/// Default <see cref="IWorkoutService"/> impl.
/// </summary>
public class WorkoutService : IWorkoutService
{
    private readonly IWorkoutStore _store;
    private readonly IAuthorizationGuard _authz;
    private readonly IOutboxDispatcher _outbox;

    public WorkoutService(
        IWorkoutStore store,
        IAuthorizationGuard authz,
        IOutboxDispatcher outbox)
    {
        _store = store;
        _authz = authz;
        _outbox = outbox;
    }

    public async Task<IReadOnlyList<WorkoutSessionView>> ListSessionsAsync(
        Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var sessions = await _store.ListSessionsAsync(userId, from, to, ct);
        return sessions.Select(MapSession).ToList();
    }

    public async Task<WorkoutSessionDetailView?> GetSessionDetailAsync(
        Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await _store.FindSessionByIdAsync(sessionId, ct);
        if (session is null || session.UserId != userId) return null;

        var exercises = await _store.ListExercisesAsync(sessionId, ct);
        var exerciseViews = new List<WorkoutSessionExerciseView>(exercises.Count);
        foreach (var ex in exercises.OrderBy(e => e.OrderIndex))
        {
            var sets = await _store.ListSetsAsync(ex.Id, ct);
            exerciseViews.Add(new WorkoutSessionExerciseView(
                ex.Id,
                ex.ExerciseName,
                ex.Type,
                ex.OrderIndex,
                ex.PlannedSets,
                ex.PlannedReps,
                ex.PlannedDurationSec,
                ex.PlannedRestSec,
                ex.PlannedCalories,
                ex.Tags,
                ex.CompletedSets,
                sets.OrderBy(s => s.SetIndex).Select(s => new WorkoutSetView(
                    s.Id, s.SetIndex, s.Status, s.ActualReps, s.ActualDurationSec, s.IsBonus)).ToList()));
        }

        return new WorkoutSessionDetailView(
            session.Id, session.WorkoutPlanId, session.SnapshotPlanName,
            session.Date, session.Day, session.DurationMin, session.CaloriesBurned,
            session.Exercises, session.Status, session.CompletedSets, session.PlannedSets,
            session.CreatedAt, exerciseViews);
    }

    public async Task<WorkoutSessionView> LogSessionAsync(
        Guid userId, LogWorkoutSessionRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "idempotency key is required.",
                new Dictionary<string, string[]> { ["idempotency_key"] = new[] { "required" } });

        ValidateLogRequest(request);

        // Resolve plan (optional) to fetch snapshot fields.
        WorkoutPlan? plan = null;
        if (request.PlanId.HasValue)
        {
            plan = await _store.FindPlanByIdAsync(request.PlanId.Value, ct)
                ?? throw new NotFoundException(WorkoutErrorCodes.PlanNotFound,
                    $"Workout plan {request.PlanId} not found.");
        }

        // Idempotency by (userId, clientId) — unique constraint at DB level.
        var existing = await _store.FindSessionByClientIdAsync(userId, request.ClientId, ct);
        if (existing is not null)
        {
            if (existing.UserId != userId)
                throw new ConflictException(WorkoutErrorCodes.DuplicateClientId,
                    $"ClientId {request.ClientId} already used by another user.");
            return MapSession(existing);
        }

        var session = WorkoutSession.Create(
            userId, request.PlanId, request.ClientId,
            request.Date, MapDay(request.Date), request.DurationMin,
            request.CaloriesBurned, request.Exercises, request.Status,
            request.CompletedSets, request.PlannedSets,
            plan?.WorkoutName, plan?.DayOfWeek,
            plan?.DurationMinutes, plan?.TargetCaloriesBurn);

        await _store.AddSessionAsync(session, ct);

        if (request.ExerciseItems is { Count: > 0 })
        {
            foreach (var item in request.ExerciseItems)
            {
                var source = await _store.FindCatalogExerciseByIdAsync(item.ExerciseId, ct);
                if (source is null)
                    throw new NotFoundException(WorkoutErrorCodes.ExerciseNotFound,
                        $"Exercise {item.ExerciseId} not found.");

                var sessionEx = WorkoutSessionExercise.Create(
                    session.Id, source.Name, source.Type, item.OrderIndex,
                    source.Sets, item.PlannedReps ?? source.Reps, source.DurationSec,
                    item.PlannedRestSec ?? source.RestSec, source.Calories,
                    source.Tags);

                await _store.AddExerciseAsync(sessionEx, ct);

                if (item.Sets is { Count: > 0 })
                {
                    foreach (var setReq in item.Sets)
                    {
                        var isBonus = setReq.SetIndex > source.Sets;
                        var set = WorkoutSessionExerciseSet.Create(
                            sessionEx.Id, setReq.SetIndex, setReq.Status,
                            setReq.ActualReps, setReq.ActualDurationSec, isBonus);
                        await _store.AddSetAsync(set, ct);
                    }
                    sessionEx.RecomputeCompletedSets();
                }
            }
        }

        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("workout.session_logged", new
        {
            sessionId = session.Id,
            userId,
            date = request.Date.ToString("O"),
            caloriesBurned = session.CaloriesBurned
        });

        return MapSession(session);
    }

    private static void ValidateLogRequest(LogWorkoutSessionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.ClientId))
            errors["client_id"] = new[] { "required" };
        if (request.DurationMin <= 0 || request.DurationMin > 600)
            errors["duration_min"] = new[] { "must be 1..600" };
        if (request.CaloriesBurned < 0 || request.CaloriesBurned > 10000)
            errors["calories_burned"] = new[] { "must be 0..10000" };
        if (request.Exercises < 0 || request.Exercises > 100)
            errors["exercises"] = new[] { "must be 0..100" };

        if (errors.Count > 0)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Invalid log request.",
                errors);
    }

    private static string MapDay(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        DayOfWeek.Sunday => "Sun",
        _ => "Mon"
    };

    private static WorkoutSessionView MapSession(WorkoutSession s) => new(
        s.Id, s.WorkoutPlanId, s.SnapshotPlanName, s.Date, s.Day,
        s.DurationMin, s.CaloriesBurned, s.Exercises, s.Status,
        s.CompletedSets, s.PlannedSets, s.CreatedAt);
}
