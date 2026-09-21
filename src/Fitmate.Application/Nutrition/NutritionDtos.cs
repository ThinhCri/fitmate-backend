using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

// ─── Query ─────────────────────────────────────────────────────────────────

public sealed record MealLogView(
    Guid Id,
    string FoodName,
    string? ImageUrl,
    int Calories,
    int ProteinG,
    int CarbsG,
    int FatG,
    decimal? PortionSize,
    MealSource Source,
    decimal? AiConfidence,
    bool Confirmed,
    DateTime LoggedAt,
    DateTime CreatedAt);

public sealed record MealScheduleView(
    Guid Id,
    string? Breakfast,
    string? Lunch,
    string? Dinner,
    string? Snack);

public sealed record NutritionScanResultView(
    IReadOnlyList<NutritionScanItemView> Items,
    int TotalCalories,
    decimal Confidence,
    string Source,
    string? Note);

public sealed record NutritionScanItemView(
    string Name,
    int Calories,
    int ProteinG,
    int CarbsG,
    int FatG);

// ─── Command ─────────────────────────────────────────────────────────────────

public sealed record LogMealRequest
{
    public required string FoodName { get; init; }
    public string? ImageUrl { get; init; }
    public required int Calories { get; init; }
    public int ProteinG { get; init; }
    public int CarbsG { get; init; }
    public int FatG { get; init; }
    public decimal? PortionSize { get; init; }
    public required MealSource Source { get; init; }
    public decimal? AiConfidence { get; init; }
    public DateTime? LoggedAt { get; init; }
}

public sealed record UpdateMealRequest
{
    public string? FoodName { get; init; }
    public int? Calories { get; init; }
    public int? ProteinG { get; init; }
    public int? CarbsG { get; init; }
    public int? FatG { get; init; }
    public decimal? PortionSize { get; init; }
    public bool? Confirmed { get; init; }
}

public sealed record UpsertScheduleRequest
{
    public string? Breakfast { get; init; }
    public string? Lunch { get; init; }
    public string? Dinner { get; init; }
    public string? Snack { get; init; }
}
