using System.Text.Json.Serialization;

namespace Fitmate.Application.Dashboard;

public sealed record DashboardSummaryView(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("nutrition")] NutritionSummaryView Nutrition,
    [property: JsonPropertyName("workout")] WorkoutSummaryView Workout,
    [property: JsonPropertyName("net_calories")] int NetCalories,
    [property: JsonPropertyName("streak")] StreakView Streak,
    [property: JsonPropertyName("latest_weight")] LatestWeightView? LatestWeight,
    [property: JsonPropertyName("upcoming_appointment")] UpcomingAppointmentSummaryView? UpcomingAppointment,
    [property: JsonPropertyName("today_workout")] TodayWorkoutSummaryView? TodayWorkout = null,
    [property: JsonPropertyName("today_meals")] IReadOnlyList<TodayMealSummaryView>? TodayMeals = null);

public sealed record NutritionSummaryView(
    [property: JsonPropertyName("total_calories")] int TotalCalories,
    [property: JsonPropertyName("protein_g")] int ProteinG,
    [property: JsonPropertyName("carbs_g")] int CarbsG,
    [property: JsonPropertyName("fat_g")] int FatG,
    [property: JsonPropertyName("target_calories")] int? TargetCalories,
    [property: JsonPropertyName("target_protein_g")] int? TargetProteinG = null,
    [property: JsonPropertyName("target_carbs_g")] int? TargetCarbsG = null,
    [property: JsonPropertyName("target_fat_g")] int? TargetFatG = null);

public sealed record WorkoutSummaryView(
    [property: JsonPropertyName("total_calories_burned")] int TotalCaloriesBurned,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("completed_sessions_count")] int CompletedSessionsCount);

public sealed record StreakView(
    [property: JsonPropertyName("current_streak_days")] int CurrentStreakDays,
    [property: JsonPropertyName("longest_streak_days")] int LongestStreakDays);

public sealed record LatestWeightView(
    [property: JsonPropertyName("weight_kg")] decimal WeightKg,
    [property: JsonPropertyName("recorded_at")] DateTime RecordedAt);

public sealed record UpcomingAppointmentSummaryView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("pt_user_id")] Guid PtUserId,
    [property: JsonPropertyName("pt_name")] string? PtName,
    [property: JsonPropertyName("slot_start_utc")] DateTime SlotStartUtc,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("status")] string Status);

public sealed record TodayWorkoutSummaryView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("resource_type")] string ResourceType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("duration_minutes")] int DurationMinutes,
    [property: JsonPropertyName("exercise_count")] int ExerciseCount,
    [property: JsonPropertyName("status")] string Status);

public sealed record TodayMealSummaryView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("food_name")] string FoodName,
    [property: JsonPropertyName("meal_slot")] string MealSlot,
    [property: JsonPropertyName("calories")] int Calories,
    [property: JsonPropertyName("logged_at")] DateTime LoggedAt);
