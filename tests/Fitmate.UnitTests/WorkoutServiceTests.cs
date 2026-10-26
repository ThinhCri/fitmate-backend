using Fitmate.Application.Auth;
using Fitmate.Application.Outbox;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-160 & BE-811: WorkoutService unit tests — plan CRUD, list sessions, get detail, log session,
/// idempotency by client_id, payload conflict 409, snapshot plan fields, and plan deletion preserving history.
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
        public List<WorkoutPlan> Plans { get; } = new();
        public List<Exercise> PlanExercises { get; } = new();
        public WorkoutPlan? Plan { get; set; }
        public Exercise? CatalogExercise { get; set; }

        public Task<WorkoutSession?> FindSessionByIdAsync(Guid sessionId, CancellationToken ct)
            => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == sessionId && !s.IsDeleted));

        public Task<WorkoutSession?> FindSessionByClientIdAsync(Guid userId, string clientId, CancellationToken ct)
            => Task.FromResult(Sessions.FirstOrDefault(s => s.UserId == userId && s.ClientId == clientId && !s.IsDeleted));

        public Task<IReadOnlyList<WorkoutSession>> ListSessionsAsync(
            Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            var q = Sessions.Where(s => s.UserId == userId && !s.IsDeleted);
            if (from.HasValue) q = q.Where(s => s.Date >= from.Value);
            if (to.HasValue) q = q.Where(s => s.Date <= to.Value);
            return Task.FromResult<IReadOnlyList<WorkoutSession>>(q.OrderByDescending(s => s.Date).ToList());
        }

        public Task<WorkoutSessionExercise?> FindSessionExerciseByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Exercises.FirstOrDefault(e => e.Id == id && !e.IsDeleted));

        public Task<IReadOnlyList<WorkoutSessionExercise>> ListExercisesAsync(Guid sessionId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkoutSessionExercise>>(Exercises.Where(e => e.WorkoutSessionId == sessionId && !e.IsDeleted).ToList());

        public Task<IReadOnlyList<WorkoutSessionExerciseSet>> ListSetsAsync(Guid exerciseId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkoutSessionExerciseSet>>(Sets.Where(s => s.WorkoutSessionExerciseId == exerciseId && !s.IsDeleted).ToList());

        public Task<WorkoutPlan?> FindPlanByIdAsync(Guid planId, CancellationToken ct)
            => Task.FromResult(Plan ?? Plans.FirstOrDefault(p => p.Id == planId && !p.IsDeleted));

        public Task<IReadOnlyList<WorkoutPlan>> ListPlansAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorkoutPlan>>(Plans.Where(p => p.UserId == userId && !p.IsDeleted).OrderBy(p => p.CreatedAt).ToList());

        public Task<IReadOnlyList<Exercise>> ListPlanExercisesAsync(Guid planId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Exercise>>(PlanExercises.Where(e => e.WorkoutPlanId == planId && !e.IsDeleted).OrderBy(e => e.CreatedAt).ToList());

        public Task<Dictionary<Guid, int>> CountPlanExercisesAsync(IReadOnlyList<Guid> planIds, CancellationToken ct)
        {
            var dict = PlanExercises
                .Where(e => planIds.Contains(e.WorkoutPlanId) && !e.IsDeleted)
                .GroupBy(e => e.WorkoutPlanId)
                .ToDictionary(g => g.Key, g => g.Count());
            return Task.FromResult(dict);
        }

        public Task<Exercise?> FindCatalogExerciseByIdAsync(Guid exerciseId, CancellationToken ct)
            => Task.FromResult(CatalogExercise);

        public Task AddPlanAsync(WorkoutPlan plan, CancellationToken ct) { Plans.Add(plan); return Task.CompletedTask; }
        public Task AddPlanExerciseAsync(Exercise exercise, CancellationToken ct) { PlanExercises.Add(exercise); return Task.CompletedTask; }
        public Task DeletePlanExercisesAsync(Guid planId, CancellationToken ct)
        {
            foreach (var ex in PlanExercises.Where(e => e.WorkoutPlanId == planId))
            {
                ex.SoftDelete();
            }
            return Task.CompletedTask;
        }
        public Task DeletePlanAsync(WorkoutPlan plan, CancellationToken ct)
        {
            plan.SoftDelete();
            return Task.CompletedTask;
        }

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
        var ex = Exercise.Create(planId, "Push-up", ExerciseType.Reps, 3, 10, null, 60, 0, 50, null, null, null, null, new List<string>());
        typeof(Exercise).GetProperty("Id")!.SetValue(ex, exerciseId);
        return ex;
    }

    // ─── Plan Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListPlansAsync_returns_empty_when_no_plans()
    {
        var result = await SUT().ListPlansAsync(UserId);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListPlansAsync_returns_user_plans_with_exercise_counts()
    {
        var store = new InMemoryWorkoutStore();
        var plan1 = CreateTestPlan(Guid.NewGuid(), UserId);
        var plan2 = CreateTestPlan(Guid.NewGuid(), UserId);
        var otherUserPlan = CreateTestPlan(Guid.NewGuid(), Guid.NewGuid());
        store.Plans.Add(plan1);
        store.Plans.Add(plan2);
        store.Plans.Add(otherUserPlan);

        var ex1 = CreateTestExercise(Guid.NewGuid(), plan1.Id);
        var ex2 = CreateTestExercise(Guid.NewGuid(), plan1.Id);
        store.PlanExercises.Add(ex1);
        store.PlanExercises.Add(ex2);

        var result = await SUT(store).ListPlansAsync(UserId);

        result.Should().HaveCount(2);
        result.First(p => p.Id == plan1.Id).ExerciseCount.Should().Be(2);
        result.First(p => p.Id == plan2.Id).ExerciseCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPlanDetailAsync_returns_null_when_not_found_or_different_user()
    {
        var store = new InMemoryWorkoutStore();
        var plan = CreateTestPlan(PlanId, Guid.NewGuid());
        store.Plans.Add(plan);

        var result = await SUT(store).GetPlanDetailAsync(UserId, plan.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanDetailAsync_returns_plan_with_exercises()
    {
        var store = new InMemoryWorkoutStore();
        var plan = CreateTestPlan(PlanId, UserId);
        store.Plans.Add(plan);
        var ex = CreateTestExercise(ExerciseId, plan.Id);
        store.PlanExercises.Add(ex);

        var result = await SUT(store).GetPlanDetailAsync(UserId, plan.Id);

        result.Should().NotBeNull();
        result!.WorkoutName.Should().Be("Morning Run");
        result.Exercises.Should().HaveCount(1);
        result.Exercises[0].Name.Should().Be("Push-up");
    }

    [Fact]
    public async Task CreatePlanAsync_creates_plan_and_exercises_and_emits_outbox()
    {
        var store = new InMemoryWorkoutStore();
        var outbox = new TestOutbox();
        var svc = SUT(store, outbox);

        var request = new CreateWorkoutPlanRequest
        {
            DayOfWeek = "Tue",
            WorkoutName = "Leg Day",
            DurationMinutes = 45,
            TargetCaloriesBurn = 350,
            Exercises = new List<WorkoutPlanExerciseInput>
            {
                new()
                {
                    Name = "Squats",
                    Type = ExerciseType.Reps,
                    Sets = 4,
                    Reps = 12,
                    RestSec = 60,
                    Calories = 100
                }
            }
        };

        var result = await svc.CreatePlanAsync(UserId, request);

        result.Should().NotBeNull();
        result.DayOfWeek.Should().Be("Tue");
        result.WorkoutName.Should().Be("Leg Day");
        result.Exercises.Should().HaveCount(1);
        result.Exercises[0].Name.Should().Be("Squats");
        store.Plans.Should().HaveCount(1);
        outbox.Messages.Should().ContainSingle(m => m.Type == "workout.plan_created");
    }

    [Fact]
    public async Task CreatePlanAsync_throws_validation_when_invalid()
    {
        var request = new CreateWorkoutPlanRequest
        {
            DayOfWeek = "",
            WorkoutName = "",
            DurationMinutes = 0,
            TargetCaloriesBurn = -1
        };

        var act = () => SUT().CreatePlanAsync(UserId, request);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    [InlineData(45000000)]
    public async Task Plan_writes_reject_invalid_rest_without_persisting(int restSeconds)
    {
        var store = new InMemoryWorkoutStore();
        var exercise = new WorkoutPlanExerciseInput { Name = "Push-up", Type = ExerciseType.Reps, Sets = 3, Reps = 10, RestSec = restSeconds };
        var create = () => SUT(store).CreatePlanAsync(UserId, new CreateWorkoutPlanRequest {
            DayOfWeek = "Mon", WorkoutName = "Test", DurationMinutes = 30, TargetCaloriesBurn = 0, Exercises = [exercise] });
        await create.Should().ThrowAsync<ValidationException>();
        store.Plans.Should().BeEmpty();
        store.PlanExercises.Should().BeEmpty();

        var plan = CreateTestPlan(PlanId, UserId);
        store.Plans.Add(plan);
        var original = CreateTestExercise(ExerciseId, plan.Id);
        store.PlanExercises.Add(original);
        var update = () => SUT(store).UpdatePlanAsync(UserId, plan.Id, new UpdateWorkoutPlanRequest { Exercises = [exercise] });
        await update.Should().ThrowAsync<ValidationException>();
        store.PlanExercises.Should().ContainSingle().Which.RestSec.Should().Be(60);
    }

    [Theory]
    [InlineData(11, 10, null)]
    [InlineData(0, 10, null)]
    [InlineData(3, 101, null)]
    [InlineData(3, 100000, null)]
    [InlineData(3, 0, null)]
    [InlineData(3, -1, null)]
    [InlineData(3, null, 601)]
    [InlineData(3, null, 0)]
    public async Task Plan_numeric_limits_reject_create_and_update_without_writes(int sets, int? reps, int? duration)
    {
        var store = new InMemoryWorkoutStore();
        var exercise = new WorkoutPlanExerciseInput { Name = "Test", Type = duration.HasValue ? ExerciseType.Time : ExerciseType.Reps, Sets = sets, Reps = reps, DurationSec = duration, RestSec = 0 };
        await FluentActions.Invoking(() => SUT(store).CreatePlanAsync(UserId, new CreateWorkoutPlanRequest {
            DayOfWeek = "Mon", WorkoutName = "Test", DurationMinutes = 30, TargetCaloriesBurn = 0, Exercises = [exercise] })).Should().ThrowAsync<ValidationException>();
        store.Plans.Should().BeEmpty();
        var plan = CreateTestPlan(PlanId, UserId); store.Plans.Add(plan);
        await FluentActions.Invoking(() => SUT(store).UpdatePlanAsync(UserId, plan.Id, new UpdateWorkoutPlanRequest { Exercises = [exercise] })).Should().ThrowAsync<ValidationException>();
        store.PlanExercises.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(100000)]
    public async Task Completed_session_rejects_out_of_range_actual_reps_before_writing(int reps)
    {
        var store = new InMemoryWorkoutStore();
        var request = new LogWorkoutSessionRequest {
            ClientId = "boundary", Date = new DateOnly(2026, 10, 27), DurationMin = 1, CaloriesBurned = 0,
            Exercises = 1, Status = WorkoutSessionStatus.Completed,
            ExerciseItems = [new() { ExerciseId = ExerciseId, OrderIndex = 0,
                Sets = [new() { SetIndex = 1, Status = WorkoutSetStatus.Completed, ActualReps = reps }] }] };
        await FluentActions.Invoking(() => SUT(store).LogSessionAsync(UserId, request, "boundary-key", CancellationToken.None)).Should().ThrowAsync<ValidationException>();
        store.Sessions.Should().BeEmpty();
        store.Sets.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 1, null)]
    [InlineData(10, 100, null)]
    [InlineData(10, null, 600)]
    public async Task Plan_accepts_numeric_boundaries(int sets, int? reps, int? duration)
    {
        var result = await SUT().CreatePlanAsync(UserId, new CreateWorkoutPlanRequest {
            DayOfWeek = "Mon", WorkoutName = "Boundary", DurationMinutes = 30, TargetCaloriesBurn = 0,
            Exercises = [new() { Name = "Test", Type = duration.HasValue ? ExerciseType.Time : ExerciseType.Reps,
                Sets = sets, Reps = reps, DurationSec = duration, RestSec = 600 }] });
        result.Exercises.Single().Sets.Should().Be(sets);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600)]
    public async Task Plan_accepts_rest_boundaries_in_seconds(int restSeconds)
    {
        var result = await SUT().CreatePlanAsync(UserId, new CreateWorkoutPlanRequest {
            DayOfWeek = "Mon", WorkoutName = "Test", DurationMinutes = 30, TargetCaloriesBurn = 0,
            Exercises = [new() { Name = "Push-up", Type = ExerciseType.Reps, Sets = 3, Reps = 10, RestSec = restSeconds }] });
        result.Exercises.Single().RestSec.Should().Be(restSeconds);
    }

    [Fact]
    public async Task UpdatePlanAsync_updates_plan_and_replaces_exercises()
    {
        var store = new InMemoryWorkoutStore();
        var outbox = new TestOutbox();
        var plan = CreateTestPlan(PlanId, UserId);
        store.Plans.Add(plan);
        var oldEx = CreateTestExercise(Guid.NewGuid(), plan.Id);
        store.PlanExercises.Add(oldEx);

        var request = new UpdateWorkoutPlanRequest
        {
            WorkoutName = "Updated Run",
            DurationMinutes = 50,
            Exercises = new List<WorkoutPlanExerciseInput>
            {
                new()
                {
                    Name = "Sprint",
                    Type = ExerciseType.Time,
                    Sets = 5,
                    DurationSec = 30,
                    RestSec = 30,
                    Calories = 80
                }
            }
        };

        var result = await SUT(store, outbox).UpdatePlanAsync(UserId, plan.Id, request);

        result.WorkoutName.Should().Be("Updated Run");
        result.DurationMinutes.Should().Be(50);
        result.Exercises.Should().HaveCount(1);
        result.Exercises[0].Name.Should().Be("Sprint");
        oldEx.IsDeleted.Should().BeTrue();
        outbox.Messages.Should().ContainSingle(m => m.Type == "workout.plan_updated");
    }

    [Fact]
    public async Task DeletePlanAsync_deletes_plan_and_exercises_preserves_session()
    {
        var store = new InMemoryWorkoutStore();
        var outbox = new TestOutbox();
        var plan = CreateTestPlan(PlanId, UserId);
        store.Plans.Add(plan);
        var ex = CreateTestExercise(Guid.NewGuid(), plan.Id);
        store.PlanExercises.Add(ex);

        // Session created with snapshot from this plan
        var session = WorkoutSession.Create(UserId, plan.Id, "c-del", new DateOnly(2026, 9, 10), "Mon",
            60, 400, 1, WorkoutSessionStatus.Completed, 3, 3,
            snapshotPlanName: plan.WorkoutName, snapshotPlanDayOfWeek: plan.DayOfWeek);
        store.Sessions.Add(session);

        await SUT(store, outbox).DeletePlanAsync(UserId, plan.Id);

        plan.IsDeleted.Should().BeTrue();
        ex.IsDeleted.Should().BeTrue();
        // Session and its snapshot data remain fully intact
        session.IsDeleted.Should().BeFalse();
        session.SnapshotPlanName.Should().Be("Morning Run");
        outbox.Messages.Should().ContainSingle(m => m.Type == "workout.plan_deleted");
    }

    // ─── Session Tests ──────────────────────────────────────────────────────

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
    public async Task LogSessionAsync_rejects_foreign_plan_as_not_found()
    {
        var store = new InMemoryWorkoutStore
        {
            Plan = CreateTestPlan(PlanId, Guid.NewGuid())
        };
        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-foreign-plan",
            PlanId = PlanId,
            Date = new DateOnly(2026, 10, 6),
            DurationMin = 20,
            CaloriesBurned = 100,
            Exercises = 1,
            Status = WorkoutSessionStatus.Completed
        };

        var act = () => SUT(store).LogSessionAsync(UserId, request, "idem-foreign", CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        store.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task LogSessionAsync_persists_set_results_and_recomputes_completed_sets()
    {
        var store = new InMemoryWorkoutStore
        {
            Plan = CreateTestPlan(PlanId, UserId),
            CatalogExercise = CreateTestExercise(ExerciseId, PlanId)
        };
        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-sets",
            PlanId = PlanId,
            Date = new DateOnly(2026, 10, 6),
            DurationMin = 20,
            CaloriesBurned = 100,
            Exercises = 1,
            CompletedSets = 2,
            PlannedSets = 3,
            Status = WorkoutSessionStatus.EndedEarly,
            ExerciseItems = new List<LogExerciseRequest>
            {
                new()
                {
                    ExerciseId = ExerciseId,
                    OrderIndex = 0,
                    Sets = new List<LogSetRequest>
                    {
                        new() { SetIndex = 1, Status = WorkoutSetStatus.Completed, ActualReps = 10 },
                        new() { SetIndex = 2, Status = WorkoutSetStatus.Completed, ActualReps = 9 },
                        new() { SetIndex = 3, Status = WorkoutSetStatus.Skipped }
                    }
                }
            }
        };

        await SUT(store).LogSessionAsync(UserId, request, "idem-sets", CancellationToken.None);

        store.Sets.Should().HaveCount(3);
        store.Exercises.Should().ContainSingle().Which.CompletedSets.Should().Be(2);
    }

    [Fact]
    public async Task LogSessionAsync_idempotent_by_client_id_returns_existing_when_same_payload()
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
    public async Task LogSessionAsync_throws_conflict_409_when_same_client_id_with_different_payload()
    {
        var store = new InMemoryWorkoutStore();
        var existing = WorkoutSession.Create(UserId, null, "c-dup-diff", new DateOnly(2026, 9, 1), "Mon", 60, 300, 2, WorkoutSessionStatus.Completed, null, null);
        store.Sessions.Add(existing);

        var request = new LogWorkoutSessionRequest
        {
            ClientId = "c-dup-diff",
            Date = new DateOnly(2026, 9, 1),
            DurationMin = 90, // Different duration
            CaloriesBurned = 500,
            Exercises = 3,
            Status = WorkoutSessionStatus.Completed
        };

        var act = () => SUT(store).LogSessionAsync(UserId, request, "idem-key", CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
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
