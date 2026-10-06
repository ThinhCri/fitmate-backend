using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Application.Nutrition;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Fitmate.IntegrationTests;

public class NutritionAndAiContractTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public NutritionAndAiContractTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Ai_Scan_Meal_Returns_503_Service_Unavailable_With_AiUnavailable_ProblemDetails()
    {
        var client = _factory.CreateAuthenticatedClient(role: "Member");

        using var content = new MultipartFormDataContent();
        var dummyBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 }; // Fake JPEG header
        var byteContent = new ByteArrayContent(dummyBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(byteContent, "file", "meal.jpg");

        var response = await client.PostAsync("/api/v1/me/nutrition/scan", content);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().BeOneOf("application/problem+json", "application/json");

        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>();
        problem.Should().NotBeNull();
        problem!.RootElement.GetProperty("status").GetInt32().Should().Be(503);
        problem.RootElement.GetProperty("code").GetString().Should().Be(ErrorCodes.AI.Unavailable);
    }

    [Fact]
    public async Task Manual_Meal_Logging_And_Listing_Roundtrip_Persists_To_Database()
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: userId);

        var logRequest = new LogMealRequest
        {
            FoodName = "Cơm tấm sườn nướng",
            Calories = 650,
            ProteinG = 32,
            CarbsG = 70,
            FatG = 20,
            PortionSize = 1,
            Source = MealSource.Manual,
            LoggedAt = DateTime.UtcNow
        };

        var postResponse = await client.PostAsJsonAsync("/api/v1/me/nutrition/meals", logRequest);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await postResponse.Content.ReadFromJsonAsync<MealLogView>();
        created.Should().NotBeNull();
        created!.FoodName.Should().Be("Cơm tấm sườn nướng");
        created.Calories.Should().Be(650);

        // Verify retrieval from PostgreSQL
        var queryDate = Uri.EscapeDataString(DateOnly.FromDateTime(logRequest.LoggedAt!.Value).ToString("yyyy-MM-dd"));
        var getResponse = await client.GetAsync(
            $"/api/v1/me/nutrition/meals?from={queryDate}&to={queryDate}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var meals = await getResponse.Content.ReadFromJsonAsync<List<MealLogView>>();
        meals.Should().NotBeNull();
        meals.Should().ContainSingle(m => m.Id == created.Id && m.FoodName == "Cơm tấm sườn nướng");
    }

    [Fact]
    public async Task Weekly_plan_create_consume_and_replay_creates_one_meal_log()
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var request = new UpsertWeeklyMealPlanItemRequest
        {
            DayOfWeek = 0,
            MealSlot = MealSlot.Breakfast,
            FoodName = "Yến mạch",
            ServingQuantity = 1,
            ServingUnit = "tô",
            Calories = 320,
            ProteinG = 12,
            CarbsG = 50,
            FatG = 8
        };

        var create = await client.PostAsJsonAsync(
            $"/api/v1/me/nutrition/weekly-plan?week_start={monday:yyyy-MM-dd}", request);
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var item = await create.Content.ReadFromJsonAsync<WeeklyMealPlanItemView>();
        item.Should().NotBeNull();

        var first = await client.PostAsJsonAsync(
            $"/api/v1/me/nutrition/weekly-plan/{item!.Id}/consume",
            new ConsumeWeeklyMealPlanItemRequest { Date = monday });
        var replay = await client.PostAsJsonAsync(
            $"/api/v1/me/nutrition/weekly-plan/{item.Id}/consume",
            new ConsumeWeeklyMealPlanItemRequest { Date = monday });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstMeal = await first.Content.ReadFromJsonAsync<MealLogView>();
        var replayMeal = await replay.Content.ReadFromJsonAsync<MealLogView>();
        replayMeal!.Id.Should().Be(firstMeal!.Id);

        var plan = await client.GetFromJsonAsync<List<WeeklyMealPlanItemView>>(
            $"/api/v1/me/nutrition/weekly-plan?week_start={monday:yyyy-MM-dd}");
        plan.Should().ContainSingle(x => x.Id == item.Id && x.IsConsumed && x.MealLogId == firstMeal.Id);
    }
}

