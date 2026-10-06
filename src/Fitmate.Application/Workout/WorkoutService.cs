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

    public async Task<IReadOnlyList<WorkoutPlanView>> ListPlansAsync(Guid userId, CancellationToken ct = default)
    {
        var plans = await _store.ListPlansAsync(userId, ct);
        if (plans.Count == 0) return Array.Empty<WorkoutPlanView>();
        var planIds = plans.Select(p => p.Id).ToList();
        var counts = await _store.CountPlanExercisesAsync(planIds, ct);
        return plans.Select(p => new WorkoutPlanView(
            p.Id,
            p.DayOfWeek,
            p.WorkoutName,
            p.DurationMinutes,
            p.TargetCaloriesBurn,
            p.IsCompleted,
            counts.GetValueOrDefault(p.Id, 0),
            p.CreatedAt)).ToList();
    }

    public async Task<WorkoutPlanDetailView?> GetPlanDetailAsync(Guid userId, Guid planId, CancellationToken ct = default)
    {
        var plan = await _store.FindPlanByIdAsync(planId, ct);
        if (plan is null || plan.UserId != userId) return null;

        var exercises = await _store.ListPlanExercisesAsync(planId, ct);
        return new WorkoutPlanDetailView(
            plan.Id,
            plan.DayOfWeek,
            plan.WorkoutName,
            plan.DurationMinutes,
            plan.TargetCaloriesBurn,
            plan.IsCompleted,
            plan.CreatedAt,
            exercises.Select(MapExerciseItem).ToList());
    }

    public async Task<WorkoutPlanDetailView> CreatePlanAsync(
        Guid userId, CreateWorkoutPlanRequest request, CancellationToken ct = default)
    {
        ValidateCreatePlanRequest(request);

        var plan = WorkoutPlan.Create(
            userId,
            request.DayOfWeek.Trim(),
            request.WorkoutName.Trim(),
            request.DurationMinutes,
            request.TargetCaloriesBurn);

        await _store.AddPlanAsync(plan, ct);

        var exerciseItems = new List<Exercise>();
        if (request.Exercises is { Count: > 0 })
        {
            foreach (var ex in request.Exercises)
            {
                var entity = Exercise.Create(
                    plan.Id,
                    ex.Name.Trim(),
                    ex.Type,
                    ex.Sets,
                    ex.Reps,
                    ex.DurationSec,
                    ex.RestSec,
                    ex.RestAfterSec,
                    ex.Calories,
                    ex.Notes,
                    ex.MediaUrl,
                    ex.MediaType,
                    ex.MediaSource,
                    ex.Tags ?? new List<string>());
                await _store.AddPlanExerciseAsync(entity, ct);
                exerciseItems.Add(entity);
            }
        }

        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("workout.plan_created", new
        {
            planId = plan.Id,
            userId
        });

        return new WorkoutPlanDetailView(
            plan.Id,
            plan.DayOfWeek,
            plan.WorkoutName,
            plan.DurationMinutes,
            plan.TargetCaloriesBurn,
            plan.IsCompleted,
            plan.CreatedAt,
            exerciseItems.Select(MapExerciseItem).ToList());
    }

    public async Task<WorkoutPlanDetailView> UpdatePlanAsync(
        Guid userId, Guid planId, UpdateWorkoutPlanRequest request, CancellationToken ct = default)
    {
        var plan = await _store.FindPlanByIdAsync(planId, ct);
        if (plan is null || plan.UserId != userId)
            throw new NotFoundException(WorkoutErrorCodes.PlanNotFound, $"Workout plan {planId} not found.");

        ValidateUpdatePlanRequest(request);

        plan.Update(
            request.DayOfWeek?.Trim(),
            request.WorkoutName?.Trim(),
            request.DurationMinutes,
            request.TargetCaloriesBurn,
            request.IsCompleted);

        var exerciseItems = new List<Exercise>();
        if (request.Exercises is not null)
        {
            await _store.DeletePlanExercisesAsync(plan.Id, ct);
            foreach (var ex in request.Exercises)
            {
                var entity = Exercise.Create(
                    plan.Id,
                    ex.Name.Trim(),
                    ex.Type,
                    ex.Sets,
                    ex.Reps,
                    ex.DurationSec,
                    ex.RestSec,
                    ex.RestAfterSec,
                    ex.Calories,
                    ex.Notes,
                    ex.MediaUrl,
                    ex.MediaType,
                    ex.MediaSource,
                    ex.Tags ?? new List<string>());
                await _store.AddPlanExerciseAsync(entity, ct);
                exerciseItems.Add(entity);
            }
        }
        else
        {
            var existing = await _store.ListPlanExercisesAsync(plan.Id, ct);
            exerciseItems.AddRange(existing);
        }

        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("workout.plan_updated", new
        {
            planId = plan.Id,
            userId
        });

        return new WorkoutPlanDetailView(
            plan.Id,
            plan.DayOfWeek,
            plan.WorkoutName,
            plan.DurationMinutes,
            plan.TargetCaloriesBurn,
            plan.IsCompleted,
            plan.CreatedAt,
            exerciseItems.Select(MapExerciseItem).ToList());
    }

    public async Task DeletePlanAsync(Guid userId, Guid planId, CancellationToken ct = default)
    {
        var plan = await _store.FindPlanByIdAsync(planId, ct);
        if (plan is null || plan.UserId != userId)
            throw new NotFoundException(WorkoutErrorCodes.PlanNotFound, $"Workout plan {planId} not found.");

        await _store.DeletePlanExercisesAsync(plan.Id, ct);
        await _store.DeletePlanAsync(plan, ct);
        await _store.SaveChangesAsync(ct);

        _outbox.Enqueue("workout.plan_deleted", new
        {
            planId = plan.Id,
            userId
        });
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
            if (plan.UserId != userId)
                throw new NotFoundException(WorkoutErrorCodes.PlanNotFound,
                    $"Workout plan {request.PlanId} not found.");
        }

        // Idempotency by (userId, clientId) — unique constraint at DB level.
        var existing = await _store.FindSessionByClientIdAsync(userId, request.ClientId, ct);
        if (existing is not null)
        {
            if (existing.UserId != userId)
                throw new ConflictException(WorkoutErrorCodes.DuplicateClientId,
                    $"ClientId {request.ClientId} already used by another user.");

            if (existing.DurationMin != request.DurationMin ||
                existing.CaloriesBurned != request.CaloriesBurned ||
                existing.Date != request.Date ||
                existing.Status != request.Status)
            {
                throw new ConflictException(WorkoutErrorCodes.DuplicateClientId,
                    $"ClientId {request.ClientId} already used with different workout payload.");
            }

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
                if (!request.PlanId.HasValue || source.WorkoutPlanId != request.PlanId.Value)
                    throw new NotFoundException(WorkoutErrorCodes.ExerciseNotFound,
                        $"Exercise {item.ExerciseId} not found in the selected workout plan.");

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
                        sessionEx.AttachSet(set);
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

    private static void ValidateCreatePlanRequest(CreateWorkoutPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.DayOfWeek))
            errors["day_of_week"] = new[] { "required" };
        else if (request.DayOfWeek.Trim().Length > 16)
            errors["day_of_week"] = new[] { "max length 16 characters" };

        if (string.IsNullOrWhiteSpace(request.WorkoutName))
            errors["workout_name"] = new[] { "required" };
        else if (request.WorkoutName.Trim().Length > 200)
            errors["workout_name"] = new[] { "max length 200 characters" };

        if (request.DurationMinutes <= 0 || request.DurationMinutes > 600)
            errors["duration_minutes"] = new[] { "must be 1..600" };

        if (request.TargetCaloriesBurn < 0 || request.TargetCaloriesBurn > 10000)
            errors["target_calories_burn"] = new[] { "must be 0..10000" };

        ValidateExerciseInputs(request.Exercises, errors);

        if (errors.Count > 0)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Invalid create plan request.",
                errors);
    }

    private static void ValidateUpdatePlanRequest(UpdateWorkoutPlanRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.DayOfWeek is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DayOfWeek))
                errors["day_of_week"] = new[] { "cannot be empty" };
            else if (request.DayOfWeek.Trim().Length > 16)
                errors["day_of_week"] = new[] { "max length 16 characters" };
        }

        if (request.WorkoutName is not null)
        {
            if (string.IsNullOrWhiteSpace(request.WorkoutName))
                errors["workout_name"] = new[] { "cannot be empty" };
            else if (request.WorkoutName.Trim().Length > 200)
                errors["workout_name"] = new[] { "max length 200 characters" };
        }

        if (request.DurationMinutes.HasValue && (request.DurationMinutes.Value <= 0 || request.DurationMinutes.Value > 600))
            errors["duration_minutes"] = new[] { "must be 1..600" };

        if (request.TargetCaloriesBurn.HasValue && (request.TargetCaloriesBurn.Value < 0 || request.TargetCaloriesBurn.Value > 10000))
            errors["target_calories_burn"] = new[] { "must be 0..10000" };

        ValidateExerciseInputs(request.Exercises, errors);

        if (errors.Count > 0)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Invalid update plan request.",
                errors);
    }

    private static void ValidateExerciseInputs(List<WorkoutPlanExerciseInput>? exercises, Dictionary<string, string[]> errors)
    {
        if (exercises is null) return;
        for (int i = 0; i < exercises.Count; i++)
        {
            var ex = exercises[i];
            if (string.IsNullOrWhiteSpace(ex.Name))
                errors[$"exercises[{i}].name"] = new[] { "required" };
            if (ex.Sets <= 0 || ex.Sets > 100)
                errors[$"exercises[{i}].sets"] = new[] { "must be 1..100" };
            if (ex.Reps.HasValue && ex.Reps.Value < 0)
                errors[$"exercises[{i}].reps"] = new[] { "cannot be negative" };
            if (ex.DurationSec.HasValue && ex.DurationSec.Value < 0)
                errors[$"exercises[{i}].duration_sec"] = new[] { "cannot be negative" };
            if (ex.RestSec < 0)
                errors[$"exercises[{i}].rest_sec"] = new[] { "cannot be negative" };
            if (ex.Calories < 0)
                errors[$"exercises[{i}].calories"] = new[] { "cannot be negative" };
        }
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

    private static ExerciseItemView MapExerciseItem(Exercise e) => new(
        e.Id, e.WorkoutPlanId, e.Name, e.Type, e.Sets, e.Reps,
        e.DurationSec, e.RestSec, e.RestAfterSec, e.Calories,
        e.Notes, e.MediaUrl, e.MediaType, e.MediaSource, e.Tags);

    private static WorkoutSessionView MapSession(WorkoutSession s) => new(
        s.Id, s.WorkoutPlanId, s.SnapshotPlanName, s.Date, s.Day,
        s.DurationMin, s.CaloriesBurned, s.Exercises, s.Status,
        s.CompletedSets, s.PlannedSets, s.CreatedAt);
}
