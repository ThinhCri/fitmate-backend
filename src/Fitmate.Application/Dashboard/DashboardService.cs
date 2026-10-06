using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Workout;

namespace Fitmate.Application.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IDashboardStore _store;

    public DashboardService(IDashboardStore store) => _store = store;

    public async Task<DashboardSummaryView> GetSummaryAsync(
        Guid userId, DateOnly? date = null, CancellationToken ct = default)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var nowUtc = DateTime.UtcNow;

        // 1. Nutrition for date
        var meals = await _store.GetMealsForDateAsync(userId, targetDate, ct);
        int totalCaloriesIn = meals.Sum(m => m.Calories);
        int totalProtein = meals.Sum(m => m.ProteinG);
        int totalCarbs = meals.Sum(m => m.CarbsG);
        int totalFat = meals.Sum(m => m.FatG);
        var preference = await _store.GetUserPreferenceAsync(userId, ct);

        var nutritionSummary = new NutritionSummaryView(
            TotalCalories: totalCaloriesIn,
            ProteinG: totalProtein,
            CarbsG: totalCarbs,
            FatG: totalFat,
            TargetCalories: preference?.TargetCalories,
            TargetProteinG: preference?.TargetProteinG,
            TargetCarbsG: preference?.TargetCarbsG,
            TargetFatG: preference?.TargetFatG);

        // 2. Workouts for date
        var sessions = await _store.GetSessionsForDateAsync(userId, targetDate, ct);
        var completedSessions = sessions.Where(s => s.Status == WorkoutSessionStatus.Completed).ToList();
        int totalCaloriesBurned = completedSessions.Sum(s => s.CaloriesBurned);
        int totalDurationMin = completedSessions.Sum(s => s.DurationMin);

        var workoutSummary = new WorkoutSummaryView(
            TotalCaloriesBurned: totalCaloriesBurned,
            DurationMinutes: totalDurationMin,
            CompletedSessionsCount: completedSessions.Count);

        var todayPlan = await _store.GetWorkoutPlanForDayAsync(userId, MapDay(targetDate), ct);
        TodayWorkoutSummaryView? todayWorkout = todayPlan is not null
            ? new TodayWorkoutSummaryView(
                todayPlan.Id, "plan", todayPlan.WorkoutName,
                todayPlan.DurationMinutes, 0, todayPlan.IsCompleted ? "Completed" : "Planned")
            : null;

        var todayMeals = meals
            .OrderByDescending(m => m.LoggedAt)
            .Select(m => new TodayMealSummaryView(
                m.Id, m.FoodName, m.MealSlot.ToString().ToLowerInvariant(), m.Calories, m.LoggedAt))
            .ToList();

        // 3. Net calories
        int netCalories = totalCaloriesIn - totalCaloriesBurned;

        // 4. Streak computation
        var completedDates = await _store.GetCompletedSessionDatesAsync(userId, ct);
        var (currentStreak, longestStreak) = ComputeStreaks(completedDates, targetDate);
        var streakView = new StreakView(currentStreak, longestStreak);

        // 5. Latest weight
        var latestWeightLog = await _store.GetLatestWeightAsync(userId, ct);
        LatestWeightView? latestWeightView = latestWeightLog is not null
            ? new LatestWeightView(latestWeightLog.WeightKg, latestWeightLog.RecordedAt)
            : null;

        // 6. Next upcoming appointment
        var nextAppt = await _store.GetNextUpcomingAppointmentAsync(userId, nowUtc, ct);
        UpcomingAppointmentSummaryView? upcomingApptView = null;
        if (nextAppt is not null)
        {
            var trainerName = await _store.GetUserNameAsync(nextAppt.PtUserId, ct);
            upcomingApptView = new UpcomingAppointmentSummaryView(
                Id: nextAppt.Id,
                PtUserId: nextAppt.PtUserId,
                PtName: trainerName,
                SlotStartUtc: nextAppt.SlotStartUtc,
                DurationMinutes: nextAppt.DurationMinutes,
                Status: nextAppt.Status.ToString());
        }

        return new DashboardSummaryView(
            Date: targetDate,
            Nutrition: nutritionSummary,
            Workout: workoutSummary,
            NetCalories: netCalories,
            Streak: streakView,
            LatestWeight: latestWeightView,
            UpcomingAppointment: upcomingApptView,
            TodayWorkout: todayWorkout,
            TodayMeals: todayMeals);
    }

    public static (int CurrentStreak, int LongestStreak) ComputeStreaks(
        IReadOnlyList<DateOnly> sortedDescDates, DateOnly targetDate)
    {
        if (sortedDescDates.Count == 0) return (0, 0);

        var dateSet = new HashSet<DateOnly>(sortedDescDates);

        // Current streak
        int currentStreak = 0;
        DateOnly checkDate = targetDate;
        if (!dateSet.Contains(checkDate))
        {
            checkDate = targetDate.AddDays(-1);
        }

        while (dateSet.Contains(checkDate))
        {
            currentStreak++;
            checkDate = checkDate.AddDays(-1);
        }

        // Longest streak
        int longestStreak = 0;
        var ascendingDates = sortedDescDates.OrderBy(d => d).ToList();
        int tempStreak = 0;
        DateOnly? prev = null;

        foreach (var d in ascendingDates)
        {
            if (prev == null)
            {
                tempStreak = 1;
            }
            else if (d == prev.Value.AddDays(1))
            {
                tempStreak++;
            }
            else if (d != prev.Value)
            {
                tempStreak = 1;
            }

            if (tempStreak > longestStreak) longestStreak = tempStreak;
            prev = d;
        }

        return (currentStreak, Math.Max(longestStreak, currentStreak));
    }

    private static string MapDay(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "Mon",
        DayOfWeek.Tuesday => "Tue",
        DayOfWeek.Wednesday => "Wed",
        DayOfWeek.Thursday => "Thu",
        DayOfWeek.Friday => "Fri",
        DayOfWeek.Saturday => "Sat",
        _ => "Sun"
    };
}

