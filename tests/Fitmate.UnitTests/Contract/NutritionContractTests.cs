using System.Text.Json;
using Fitmate.Application.Nutrition;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class NutritionContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void MealLogView_Serializes_WithExactExpectedKeys_AndSnakeCaseEnum()
    {
        var meal = new MealLogView(
            Id: Guid.NewGuid(),
            FoodName: "Grilled Chicken Salad",
            ImageUrl: "https://example.com/salad.jpg",
            Calories: 450,
            ProteinG: 40,
            CarbsG: 15,
            FatG: 12,
            PortionSize: 300m,
            Source: MealSource.AiEstimate,
            AiConfidence: 0.95m,
            Confirmed: true,
            LoggedAt: DateTime.UtcNow,
            CreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(meal);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "food_name",
            "image_url",
            "calories",
            "protein_g",
            "carbs_g",
            "fat_g",
            "portion_size",
            "source",
            "ai_confidence",
            "confirmed",
            "logged_at",
            "created_at"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        root.GetProperty("source").GetString().Should().Be("ai_estimate");
        root.GetProperty("food_name").GetString().Should().Be("Grilled Chicken Salad");
    }

    [Fact]
    public void MealScheduleView_Serializes_WithExactExpectedKeys()
    {
        var schedule = new MealScheduleView(
            Id: Guid.NewGuid(),
            Breakfast: "Oatmeal with berries",
            Lunch: "Chicken and brown rice",
            Dinner: "Salmon with asparagus",
            Snack: "Almonds and apple"
        );

        var json = JsonSerializer.Serialize(schedule);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "id", "breakfast", "lunch", "dinner", "snack" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void NutritionScanResultView_Serializes_WithExactExpectedKeys()
    {
        var result = new NutritionScanResultView(
            Items: new List<NutritionScanItemView>
            {
                new("Pho Bo", 500, 25, 60, 15)
            },
            TotalCalories: 500,
            Confidence: 0.92m,
            Source: "ai_estimate",
            Note: "Estimated from camera capture"
        );

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "items", "total_calories", "confidence", "source", "note" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        var item = root.GetProperty("items")[0];
        item.GetProperty("protein_g").GetInt32().Should().Be(25);
    }

    [Fact]
    public void LogMealRequest_Deserializes_FromSnakeCase()
    {
        var json = """
        {
            "food_name": "Egg toast",
            "image_url": null,
            "calories": 250,
            "protein_g": 14,
            "carbs_g": 20,
            "fat_g": 10,
            "portion_size": 150,
            "source": "manual",
            "ai_confidence": null,
            "logged_at": "2026-10-02T08:00:00Z"
        }
        """;

        var req = JsonSerializer.Deserialize<LogMealRequest>(json);
        req.Should().NotBeNull();
        req!.FoodName.Should().Be("Egg toast");
        req.Source.Should().Be(MealSource.Manual);
        req.Calories.Should().Be(250);
    }

    [Fact]
    public void OpenApi_NutritionSchemas_MatchExpectedProperties()
    {
        File.Exists(OpenApiPath).Should().BeTrue();
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);
        diagnostic.Errors.Should().BeEmpty();

        doc.Components.Schemas.Should().ContainKey("MealLog");
        var mealSchema = doc.Components.Schemas["MealLog"];
        var expectedMealProperties = new HashSet<string>
        {
            "id",
            "food_name",
            "image_url",
            "calories",
            "protein_g",
            "carbs_g",
            "fat_g",
            "portion_size",
            "source",
            "ai_confidence",
            "confirmed",
            "logged_at",
            "created_at"
        };
        mealSchema.Properties.Keys.Should().BeEquivalentTo(expectedMealProperties);

        doc.Components.Schemas.Should().ContainKey("MealSchedule");
        var scheduleSchema = doc.Components.Schemas["MealSchedule"];
        var expectedScheduleProperties = new HashSet<string>
        {
            "id",
            "breakfast",
            "lunch",
            "dinner",
            "snack"
        };
        scheduleSchema.Properties.Keys.Should().BeEquivalentTo(expectedScheduleProperties);
    }
}
