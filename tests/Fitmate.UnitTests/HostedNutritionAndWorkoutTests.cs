using System.Net;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Chat;
using Fitmate.Infrastructure.Nutrition;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

public sealed class HostedNutritionAndWorkoutTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task FoodScan_ReturnsOnlyProviderEstimatesAndRequiresExplicitConfirmationElsewhere()
    {
        using var http = new HttpClient(new Handler(async request => {
            request.RequestUri!.Host.Should().Be("generativelanguage.googleapis.com");
            request.Headers.Contains("x-goog-api-key").Should().BeTrue();
            (await request.Content!.ReadAsStringAsync()).Should().Contain("inlineData");
            return Json(new { candidates = new[] { new { content = new { parts = new[] { new { text = "{\"items\":[{\"name\":\"Cơm\",\"calories\":250,\"protein_g\":4,\"carbs_g\":55,\"fat_g\":1}],\"ingredients\":[\"gạo\"]}" } } } } } });
        }));
        var adapter = new GeminiNutritionAdapter(http, Options.Create(new GeminiOptions { Enabled = true, ApiKey = "unit-test", Model = "test-model" }), Options.Create(new AiOptions { Enabled = true, FoodScanEnabled = true }));
        using var image = new MemoryStream([255, 216, 255]);
        var result = await adapter.EstimateFromImageAsync(image, "image/jpeg", default);
        result.TotalCalories.Should().Be(250); result.Confidence.Should().BeNull(); result.Source.Should().Be("ai_estimate");
    }

    [Fact]
    public async Task FoodScan_MissingNutrients_IsRejectedInsteadOfInventingZeros()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new { candidates = new[] { new { content = new { parts = new[] { new { text = "{\"items\":[{\"name\":\"Food\",\"calories\":100}],\"ingredients\":[]}" } } } } } }))));
        var adapter = new GeminiNutritionAdapter(http, Options.Create(new GeminiOptions { Enabled = true, ApiKey = "unit-test", Model = "test-model" }), Options.Create(new AiOptions { Enabled = true, FoodScanEnabled = true }));
        using var image = new MemoryStream([255, 216, 255]);
        var act = () => adapter.EstimateFromImageAsync(image, "image/jpeg", default);
        await act.Should().ThrowAsync<AiInvalidOutputException>();
    }

    [Fact]
    public async Task Workout_UsesActualJsonResponseWithoutSavingAnyPlanOrInventingConfidence()
    {
        var owner = Guid.NewGuid();
        using var http = new HttpClient(new Handler(async request => {
            var body = await request.Content!.ReadAsStringAsync();
            body.Should().Contain("json_object").And.NotContain(owner.ToString());
            return Json(new { choices = new[] { new { message = new { content = "{\"name\":\"Tập nhẹ\",\"description\":\"Tham khảo\",\"exercises\":[{\"name\":\"Squat\",\"sets\":2,\"reps\":8,\"rest_seconds\":60,\"notes\":\"Giữ tư thế\"}]}" } } } });
        }));
        var adapter = new GroqWorkoutAdapter(http, Options.Create(new GroqOptions { Enabled = true, ApiKey = "unit-test", Model = "test-model" }), Options.Create(new AiOptions { Enabled = true, WorkoutAdjustEnabled = true }));
        var result = await adapter.SuggestWorkoutPlanAsync(new WorkoutSuggestionRequest(owner, "vận động", "mới"), default);
        result.Exercises.Should().ContainSingle().Which.Reps.Should().Be(8); result.Confidence.Should().BeNull(); result.Source.Should().Be("groq");
    }
}
