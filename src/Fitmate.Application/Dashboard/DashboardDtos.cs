namespace Fitmate.Application.Dashboard;

public sealed record DashboardSummaryView(
    DateOnly Date,
    NutritionSummaryView Nutrition,
    WorkoutSummaryView Workout,
    int NetCalories,
    StreakView Streak,
    LatestWeightView? LatestWeight,
    UpcomingAppointmentSummaryView? UpcomingAppointment);

public sealed record NutritionSummaryView(
    int TotalCalories,
    int ProteinG,
    int CarbsG,
    int FatG,
    int? TargetCalories);

public sealed record WorkoutSummaryView(
    int TotalCaloriesBurned,
    int DurationMinutes,
    int CompletedSessionsCount);

public sealed record StreakView(
    int CurrentStreakDays,
    int LongestStreakDays);

public sealed record LatestWeightView(
    decimal WeightKg,
    DateTime RecordedAt);

public sealed record UpcomingAppointmentSummaryView(
    Guid Id,
    Guid PtUserId,
    string? PtName,
    DateTime SlotStartUtc,
    int DurationMinutes,
    string Status);

