using Fitmate.Application.Dashboard;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Workout;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class DashboardServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly Guid TrainerId = Guid.NewGuid();

    private sealed class InMemoryDashboardStore : IDashboardStore
    {
        public List<MealLog> Meals { get; } = new();
        public List<WorkoutSession> Sessions { get; } = new();
        public List<WeightLog> WeightLogs { get; } = new();
        public List<Appointment> Appointments { get; } = new();
        public Dictionary<Guid, string> UserNames { get; } = new();

        public Task<IReadOnlyList<MealLog>> GetMealsForDateAsync(Guid userId, DateOnly date, CancellationToken ct)
        {
            var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var list = Meals
                .Where(m => m.UserId == userId && !m.IsDeleted && m.LoggedAt >= startUtc && m.LoggedAt <= endUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<MealLog>>(list);
        }

        public Task<IReadOnlyList<WorkoutSession>> GetSessionsForDateAsync(Guid userId, DateOnly date, CancellationToken ct)
        {
            var list = Sessions
                .Where(s => s.UserId == userId && !s.IsDeleted && s.Date == date)
                .ToList();
            return Task.FromResult<IReadOnlyList<WorkoutSession>>(list);
        }

        public Task<IReadOnlyList<DateOnly>> GetCompletedSessionDatesAsync(Guid userId, CancellationToken ct)
        {
            var list = Sessions
                .Where(s => s.UserId == userId && !s.IsDeleted && s.Status == WorkoutSessionStatus.Completed)
                .Select(s => s.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();
            return Task.FromResult<IReadOnlyList<DateOnly>>(list);
        }

        public Task<WeightLog?> GetLatestWeightAsync(Guid userId, CancellationToken ct)
        {
            var item = WeightLogs
                .Where(w => w.UserId == userId && !w.IsDeleted)
                .OrderByDescending(w => w.RecordedAt)
                .FirstOrDefault();
            return Task.FromResult(item);
        }

        public Task<Appointment?> GetNextUpcomingAppointmentAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
        {
            var item = Appointments
                .Where(a => a.MemberId == userId && !a.IsDeleted &&
                            (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.InProgress) &&
                            a.SlotStartUtc >= nowUtc)
                .OrderBy(a => a.SlotStartUtc)
                .FirstOrDefault();
            return Task.FromResult(item);
        }

        public Task<string?> GetUserNameAsync(Guid userId, CancellationToken ct)
        {
            UserNames.TryGetValue(userId, out var name);
            return Task.FromResult(name);
        }
    }

    [Fact]
    public async Task GetSummary_returns_empty_defaults_when_no_data()
    {
        var store = new InMemoryDashboardStore();
        var sut = new DashboardService(store);
        var date = new DateOnly(2026, 9, 23);

        var result = await sut.GetSummaryAsync(UserId, date);

        result.Date.Should().Be(date);
        result.Nutrition.TotalCalories.Should().Be(0);
        result.Nutrition.ProteinG.Should().Be(0);
        result.Nutrition.CarbsG.Should().Be(0);
        result.Nutrition.FatG.Should().Be(0);
        result.Workout.TotalCaloriesBurned.Should().Be(0);
        result.Workout.DurationMinutes.Should().Be(0);
        result.Workout.CompletedSessionsCount.Should().Be(0);
        result.NetCalories.Should().Be(0);
        result.Streak.CurrentStreakDays.Should().Be(0);
        result.Streak.LongestStreakDays.Should().Be(0);
        result.LatestWeight.Should().BeNull();
        result.UpcomingAppointment.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_aggregates_meals_and_completed_workouts()
    {
        var store = new InMemoryDashboardStore();
        var date = new DateOnly(2026, 9, 23);
        var todayTime = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

        // Meals
        store.Meals.Add(MealLog.Create(UserId, "Oatmeal", null, 300, 10, 45, 5, null, MealSource.Manual, null, false, todayTime));
        store.Meals.Add(MealLog.Create(UserId, "Chicken Rice", null, 600, 40, 60, 15, null, MealSource.Manual, null, false, todayTime));
        // Other user's meal
        store.Meals.Add(MealLog.Create(OtherUserId, "Burger", null, 800, 25, 70, 30, null, MealSource.Manual, null, false, todayTime));

        // Workouts
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "c1", date, "Wed", 45, 350, 3, WorkoutSessionStatus.Completed, 3, 3));
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "c2", date, "Wed", 15, 100, 1, WorkoutSessionStatus.Skipped, 0, 1)); // Skipped not counted in totals

        var sut = new DashboardService(store);
        var result = await sut.GetSummaryAsync(UserId, date);

        result.Nutrition.TotalCalories.Should().Be(900);
        result.Nutrition.ProteinG.Should().Be(50);
        result.Nutrition.CarbsG.Should().Be(105);
        result.Nutrition.FatG.Should().Be(20);
        result.Workout.TotalCaloriesBurned.Should().Be(350);
        result.Workout.DurationMinutes.Should().Be(45);
        result.Workout.CompletedSessionsCount.Should().Be(1);
        result.NetCalories.Should().Be(550); // 900 - 350
    }

    [Fact]
    public async Task GetSummary_computes_streak_correctly()
    {
        var store = new InMemoryDashboardStore();
        var today = new DateOnly(2026, 9, 23);

        // 3 consecutive days: Sep 23, Sep 22, Sep 21
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "s1", today, "Wed", 30, 200, 2, WorkoutSessionStatus.Completed, null, null));
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "s2", today.AddDays(-1), "Tue", 30, 200, 2, WorkoutSessionStatus.Completed, null, null));
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "s3", today.AddDays(-2), "Mon", 30, 200, 2, WorkoutSessionStatus.Completed, null, null));
        // Past streak of 5 days: Sep 10..14
        for (int i = 0; i < 5; i++)
        {
            var d = new DateOnly(2026, 9, 10).AddDays(i);
            store.Sessions.Add(WorkoutSession.Create(UserId, null, $"p{i}", d, "Day", 30, 200, 2, WorkoutSessionStatus.Completed, null, null));
        }

        var sut = new DashboardService(store);
        var result = await sut.GetSummaryAsync(UserId, today);

        result.Streak.CurrentStreakDays.Should().Be(3);
        result.Streak.LongestStreakDays.Should().Be(5);
    }

    [Fact]
    public async Task GetSummary_streak_continues_if_today_not_worked_out_yet_but_yesterday_was()
    {
        var store = new InMemoryDashboardStore();
        var today = new DateOnly(2026, 9, 23);

        // Sep 22, Sep 21
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "s1", today.AddDays(-1), "Tue", 30, 200, 2, WorkoutSessionStatus.Completed, null, null));
        store.Sessions.Add(WorkoutSession.Create(UserId, null, "s2", today.AddDays(-2), "Mon", 30, 200, 2, WorkoutSessionStatus.Completed, null, null));

        var sut = new DashboardService(store);
        var result = await sut.GetSummaryAsync(UserId, today);

        result.Streak.CurrentStreakDays.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_returns_latest_weight_and_upcoming_appointment()
    {
        var store = new InMemoryDashboardStore();
        var today = new DateOnly(2026, 9, 23);
        var now = DateTime.UtcNow;

        store.WeightLogs.Add(WeightLog.Create(UserId, 72.0m, now.AddDays(-3)));
        store.WeightLogs.Add(WeightLog.Create(UserId, 71.5m, now.AddDays(-1)));

        var appt = Appointment.CreateHold(UserId, TrainerId, Guid.NewGuid(), now.AddHours(2), now.AddHours(2), 60, 200000);
        appt.AttachPayment(Guid.NewGuid(), 200000, 0, 0.1m, 20000, 180000, "v1");
        appt.ConfirmByPt();
        store.Appointments.Add(appt);
        store.UserNames[TrainerId] = "Coach Alex";

        var sut = new DashboardService(store);
        var result = await sut.GetSummaryAsync(UserId, today);

        result.LatestWeight.Should().NotBeNull();
        result.LatestWeight!.WeightKg.Should().Be(71.5m);
        result.UpcomingAppointment.Should().NotBeNull();
        result.UpcomingAppointment!.PtName.Should().Be("Coach Alex");
        result.UpcomingAppointment.DurationMinutes.Should().Be(60);
    }
}

