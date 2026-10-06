using System.Net;
using System.Text;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Nutrition;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

public sealed class PythonAiNutritionAdapterTests
{
    [Fact]
    public async Task EstimateFromImageAsync_ForwardsMultipartAndReturnsValidatedResult()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            var body = await request.Content!.ReadAsStringAsync();
            body.Should().Contain("name=file");
            return Json(HttpStatusCode.OK,
                """{"ingredients":["cơm","gà"],"portion_estimate_g":350,"calories_kcal":520,"protein_g":32,"carbs_g":65,"fat_g":14}""");
        });
        var adapter = CreateAdapter(handler);
        using var image = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await adapter.EstimateFromImageAsync(image, "image/jpeg", CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Bữa ăn đã nhận diện");
        result.Ingredients.Should().Equal("cơm", "gà");
        result.Confidence.Should().BeNull();
        captured!.RequestUri.Should().Be("http://ai.test/predict");
        captured.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("test-token");
    }

    [Fact]
    public async Task EstimateFromImageAsync_MapsRateLimit()
    {
        var adapter = CreateAdapter(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        using var image = new MemoryStream(new byte[] { 1 });

        var act = () => adapter.EstimateFromImageAsync(image, "image/png", CancellationToken.None);

        await act.Should().ThrowAsync<AiRateLimitedException>();
    }

    [Fact]
    public async Task EstimateFromImageAsync_RejectsInvalidProviderOutput()
    {
        var adapter = CreateAdapter(new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"ingredients":[],"portion_estimate_g":0,"calories_kcal":-1,"protein_g":0,"carbs_g":0,"fat_g":0}"""))));
        using var image = new MemoryStream(new byte[] { 1 });

        var act = () => adapter.EstimateFromImageAsync(image, "image/webp", CancellationToken.None);

        await act.Should().ThrowAsync<AiInvalidOutputException>();
    }

    [Fact]
    public async Task EstimateFromImageAsync_RoundsProviderNumbersHalfUp()
    {
        var adapter = CreateAdapter(new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"ingredients":["rice"],"portion_estimate_g":350.5,"calories_kcal":520.5,"protein_g":32.5,"carbs_g":65.5,"fat_g":14.5}"""))));
        using var image = new MemoryStream(new byte[] { 1 });

        var result = await adapter.EstimateFromImageAsync(image, "image/jpeg", CancellationToken.None);

        result.TotalCalories.Should().Be(521);
        result.Items.Single().ProteinG.Should().Be(33);
    }

    [Fact]
    public async Task EstimateFromImageAsync_ReturnsEveryUniqueIngredientWithoutAbbreviation()
    {
        var adapter = CreateAdapter(new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.OK,
            """{"ingredients":["egg","lettuce","tomato","cucumber","egg"],"portion_estimate_g":300,"calories_kcal":280,"protein_g":18,"carbs_g":14,"fat_g":16}"""))));
        using var image = new MemoryStream(new byte[] { 1 });

        var result = await adapter.EstimateFromImageAsync(image, "image/jpeg", CancellationToken.None);

        result.Ingredients.Should().Equal("egg", "lettuce", "tomato", "cucumber");
        result.Items.Single().Name.Should().NotContain("+");
        result.Items.Single().Name.Should().NotContain("nguyên liệu");
    }

    [Fact]
    public async Task EstimateFromImageAsync_RequiresEnabledConfiguration()
    {
        var options = Options.Create(new AiOptions { Provider = "python_service" });
        var adapter = new PythonAiNutritionAdapter(new HttpClient(new StubHandler(_ =>
            throw new InvalidOperationException("HTTP must not be called"))), options);
        using var image = new MemoryStream(new byte[] { 1 });

        var act = () => adapter.EstimateFromImageAsync(image, "image/jpeg", CancellationToken.None);

        await act.Should().ThrowAsync<AiUnavailableException>();
    }

    private static PythonAiNutritionAdapter CreateAdapter(HttpMessageHandler handler)
    {
        var options = Options.Create(new AiOptions
        {
            Enabled = true,
            FoodScanEnabled = true,
            Provider = "python_service",
            BaseUrl = "http://ai.test",
            InternalToken = "test-token"
        });
        return new PythonAiNutritionAdapter(new HttpClient(handler), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
