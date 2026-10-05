using System.Text.Json;
using System.Text.Json.Serialization;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

public sealed class MealSourceJsonConverter : JsonStringEnumConverter<MealSource>
{
    public MealSourceJsonConverter() : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: true) { }
}

// ─── Query ─────────────────────────────────────────────────────────────────

public sealed record MealLogView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("food_name")] string FoodName,
    [property: JsonPropertyName("image_url")] string? ImageUrl,
    [property: JsonPropertyName("calories")] int Calories,
    [property: JsonPropertyName("protein_g")] int ProteinG,
    [property: JsonPropertyName("carbs_g")] int CarbsG,
    [property: JsonPropertyName("fat_g")] int FatG,
    [property: JsonPropertyName("portion_size")] decimal? PortionSize,
    [property: JsonPropertyName("source")]
    [property: JsonConverter(typeof(MealSourceJsonConverter))] MealSource Source,
    [property: JsonPropertyName("ai_confidence")] decimal? AiConfidence,
    [property: JsonPropertyName("confirmed")] bool Confirmed,
    [property: JsonPropertyName("logged_at")] DateTime LoggedAt,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public sealed record MealScheduleView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("breakfast")] string? Breakfast,
    [property: JsonPropertyName("lunch")] string? Lunch,
    [property: JsonPropertyName("dinner")] string? Dinner,
    [property: JsonPropertyName("snack")] string? Snack);

public sealed record NutritionScanResultView(
    [property: JsonPropertyName("items")] IReadOnlyList<NutritionScanItemView> Items,
    [property: JsonPropertyName("total_calories")] int TotalCalories,
    [property: JsonPropertyName("confidence")] decimal? Confidence,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("note")] string? Note);

public sealed record NutritionScanItemView(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("calories")] int Calories,
    [property: JsonPropertyName("protein_g")] int ProteinG,
    [property: JsonPropertyName("carbs_g")] int CarbsG,
    [property: JsonPropertyName("fat_g")] int FatG);

// ─── Command ─────────────────────────────────────────────────────────────────

public sealed record LogMealRequest
{
    [JsonPropertyName("food_name")]
    public required string FoodName { get; init; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("calories")]
    public required int Calories { get; init; }

    [JsonPropertyName("protein_g")]
    public int ProteinG { get; init; }

    [JsonPropertyName("carbs_g")]
    public int CarbsG { get; init; }

    [JsonPropertyName("fat_g")]
    public int FatG { get; init; }

    [JsonPropertyName("portion_size")]
    public decimal? PortionSize { get; init; }

    [JsonPropertyName("source")]
    [JsonConverter(typeof(MealSourceJsonConverter))]
    public required MealSource Source { get; init; }

    [JsonPropertyName("ai_confidence")]
    public decimal? AiConfidence { get; init; }

    [JsonPropertyName("logged_at")]
    public DateTime? LoggedAt { get; init; }
}

public sealed record UpdateMealRequest
{
    [JsonPropertyName("food_name")]
    public string? FoodName { get; init; }

    [JsonPropertyName("calories")]
    public int? Calories { get; init; }

    [JsonPropertyName("protein_g")]
    public int? ProteinG { get; init; }

    [JsonPropertyName("carbs_g")]
    public int? CarbsG { get; init; }

    [JsonPropertyName("fat_g")]
    public int? FatG { get; init; }

    [JsonPropertyName("portion_size")]
    public decimal? PortionSize { get; init; }

    [JsonPropertyName("confirmed")]
    public bool? Confirmed { get; init; }
}

public sealed record UpsertScheduleRequest
{
    [JsonPropertyName("breakfast")]
    public string? Breakfast { get; init; }

    [JsonPropertyName("lunch")]
    public string? Lunch { get; init; }

    [JsonPropertyName("dinner")]
    public string? Dinner { get; init; }

    [JsonPropertyName("snack")]
    public string? Snack { get; init; }
}
