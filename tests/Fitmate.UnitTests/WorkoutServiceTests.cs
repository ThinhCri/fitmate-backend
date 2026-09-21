using Fitmate.Application.Auth;
using Fitmate.Application.Outbox;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-160: WorkoutService unit tests — list sessions, get detail, log session,
/// idempotency by client_id, snapshot plan fields.
/// </summary>
public class WorkoutServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PlanId = Guid.NewGuid();
    private static readonly Guid ExerciseId = Guid.NewGuid();

    private sealed class InMemoryWorkoutStore : IWorkoutStore
    {
        public List<WorkoutSession> Sessions { get; } = new();
        public List<WorkoutSessionExercise> Exercises { get; } = new();
        public List<WorkoutSessionExerciseSet> Sets { get; } = new();
        public WorkoutPlan? Plan { get; set; }
        public Exercise? CatalogExercise { get; set; }

        public Task<WorkoutSession?> FindSessionByIdAsync(Guid sessionId, CancellationToken ct)
            => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == sessionId));

        public Task<WorkoutSession?> FindSessionByClientIdAsync(Guid userId, string clientId, CancellationToken ct)
            => Task.FromResult(Sessions.FirstOrDefault(s => s.UserId == userId && s.ClientId == clientId));

        public Task<IReadOnlyList<WorkoutSession>> ListSessionsAsync(
            Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            var q = Sessions.Where(s => s.UserId == userId);
            if (from.HasValue) q = q.Where(s => s.Date >= from.Value);
            if (to.HasValue) q = q.Where(s => s.Date <= to.Value);
            return Task.FromResult<IReadOnlyList<WorkoutSession>>(q.OrderByDescending(s => s.Date).ToList());
        }

        public Task<WorkoutSessionExercise?> FindSessionExerciseByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Exercises.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<WorkoutSessionExercise>> ListExercisesAsync(Guid sessionId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkoutSessionExercise>>(Exercises.Where(e => e.WorkoutSessionId == sessionId).ToList());

        public Task<IReadOnlyList<WorkoutSessionExerciseSet>> ListSetsAsync(Guid exerciseId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkoutSessionExerciseSet>>(Sets.Where(s => s.WorkoutSessionExerciseId == exerciseId).ToList());

        public Task<WorkoutPlan?> FindPlanByIdAsync(Guid planId, CancellationToken ct)
            => Task.FromResult(Plan);

        public Task<Exercise?> FindCatalogExerciseByIdAsync(Guid exerciseId, CancellationToken ct)
            => Task.FromResult(CatalogExercise);

        public Task AddSessionAsync(WorkoutSession session, CancellationToken ct) { Sessions.Add(session); return Task.CompletedTask; }
        public Task AddExerciseAsync(WorkoutSessionExercise exercise, CancellationToken ct) { Exercises.Add(exercise); return Task.CompletedTask; }
        public Task AddSetAsync(WorkoutSessionExerciseSet set, CancellationToken ct) { Sets.Add(set); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestOutbox : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Messages { get; } = new();
        public void Enqueue(string type, object payload) => Messages.Add((type, payload));
    }

    private static WorkoutService SUT(InMemoryWorkoutStore? store = null, TestOutbox? outbox = null)
    {
        store ??= new InMemoryWorkoutStore();
        outbox ??= new TestOutbox();
        return new WorkoutService(store, AlwaysAllowAuthz.Instance, outbox);
    }

    private static WorkoutPlan CreateTestPlan(Guid planId, Guid userId)
    {
        var plan = WorkoutPlan.Create(userId, "Mon", "Morning Run", 60, 400);
        typeof(WorkoutPlan).GetProperty("Id")!.SetValue(plan, planId);
        return plan;
    }

    private static Exercise CreateTestExercise(Guid exerciseId, Guid planId)
    {
        var ex = Exercise.Create(planId, "Push-up", ExerciseType.Reps, 3, 10, null, 60, 0, 50, null, null, null, null, null);
        typeof(Exercise).GetProperty("Id")!.SetValue(ex, exerciseId);
        return ex;
    }

    [Fact]
    public async Task ListSessionsAsync_returns_empty_when_no_sessions()
    {
        var result = await SUT().ListSessionsAsync(UserId, null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSessionsAsync_filters_by_date_range()
    {
        var store = new InMemoryWorkoutStore();
        var s1 = WorkoutSession.Create(UserId, null, "c1", new DateOnly(2026, 9, 1), "Mon", 60, 300, 2, WorkoutSessionStatus.Completed, null, null);
        var s2 = WorkoutSession.Create(UserId, null, "c2", new DateOnly(2026, 9, 20), "Mon", 45, 200, 1, WorkoutSessionStatus.Completed, null, null);
        store.Sessions.Add(s1);
        store.Sessions.Add(s2);

        var result = await SUT(store).ListSessionsAsync(
            UserId, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 30));

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(s2.Id);
    }

    [Fact]
    public async Task GetSessionDetailAsync_returns_null_for_other_user_session()
    {
        var otherUserId = Guid.NewGuid();
        var store = new InMemoryWorkoutStore();
        var session = WorkoutSession.Create(otherUserId, null, "c1", new DateOnly(2026, 9, 1), "Mon", 60, 300, 2, WorkoutSessionStatus.Completed, null, null);
        store.Sessions.Add(session);

        var result = await SUT(store).GetSessionDetailAsync(UserId, session.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSessionDetailAsync_returns_null_when_not_found()
    {
        var result = await SUT().GetSessionDetailAsync(UserId, Guid.NewGuid());
        result.Should().BeNull();
    }

    [Fact]
    public async Task LogSessionAsync_creates_session_with_snapshot_fields()
    {
        var store = new InMemoryWorkoutStore();
        store.Plan = CreateTestPlan(PlanId, UserId);

        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-new",
            PlanId = PlanId,
            Date = new DateOnly(2026, 9, 15),
            DurationMin = 60,
            CaloriesBurned = 400,
            Exercises = 3,
            Status = WorkoutSessionStatus.Completed
        };

        var result = await SUT(store).LogSessionAsync(UserId, request, "idem-key", CancellationToken.None);

        result.Should().NotBeNull();
        result.DurationMin.Should().Be(60);
        result.PlanName.Should().Be("Morning Run");
        store.Sessions.Should().HaveCount(1);
    }

    [Fact]
    public async Task LogSessionAsync_idempotent_by_client_id_returns_existing()
    {
        var store = new InMemoryWorkoutStore();
        var existing = WorkoutSession.Create(UserId, null, "c-dup", new DateOnly(2026, 9, 1), "Mon", 60, 300, 2, WorkoutSessionStatus.Completed, null, null);
        store.Sessions.Add(existing);

        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-dup",
            Date = new DateOnly(2026, 9, 1),
            DurationMin = 60,
            CaloriesBurned = 300,
            Exercises = 2,
            Status = WorkoutSessionStatus.Completed
        };

        var result = await SUT(store).LogSessionAsync(UserId, request, "idem-key", CancellationToken.None);

        result.Id.Should().Be(existing.Id);
        store.Sessions.Should().HaveCount(1);
    }

    [Fact]
    public async Task LogSessionAsync_throws_when_plan_not_found()
    {
        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-new",
            PlanId = Guid.NewGuid(),
            Date = new DateOnly(2026, 9, 15),
            DurationMin = 60,
            CaloriesBurned = 400,
            Exercises = 3,
            Status = WorkoutSessionStatus.Completed
        };

        var act = () => SUT().LogSessionAsync(UserId, request, "idem-key", CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task LogSessionAsync_throws_validation_when_duration_invalid()
    {
        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-bad",
            Date = new DateOnly(2026, 9, 15),
            DurationMin = 0,
            CaloriesBurned = 400,
            Exercises = 3,
            Status = WorkoutSessionStatus.Completed
        };

        var act = () => SUT().LogSessionAsync(UserId, request, "idem-key", CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task LogSessionAsync_logs_outbox_event()
    {
        var store = new InMemoryWorkoutStore();
        var outbox = new TestOutbox();
        var svc = new WorkoutService(store, AlwaysAllowAuthz.Instance, outbox);

        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-outbox",
            Date = new DateOnly(2026, 9, 15),
            DurationMin = 60,
            CaloriesBurned = 400,
            Exercises = 3,
            Status = WorkoutSessionStatus.Completed
        };

        await svc.LogSessionAsync(UserId, request, "idem-key", CancellationToken.None);

        outbox.Messages.Should().ContainSingle(m => m.Type == "workout.session_logged");
    }
}
