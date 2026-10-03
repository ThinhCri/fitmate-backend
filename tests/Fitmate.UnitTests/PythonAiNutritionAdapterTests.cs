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
                """{"items":[{"name":"Cơm gà","calories":520,"protein_g":32,"carbs_g":65,"fat_g":14}],"total_calories":520,"confidence":0.87,"source":"ai_estimate","note":"estimate"}""");
        });
        var adapter = CreateAdapter(handler);
        using var image = new MemoryStream(new byte[] { 1, 2, 3 });

        var result = await adapter.EstimateFromImageAsync(image, "image/jpeg", CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Cơm gà");
        captured!.RequestUri.Should().Be("http://ai.test/v1/nutrition/analyze");
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
            """{"items":[],"total_calories":-1,"confidence":2,"source":"unknown","note":null}"""))));
        using var image = new MemoryStream(new byte[] { 1 });

        var act = () => adapter.EstimateFromImageAsync(image, "image/webp", CancellationToken.None);

        await act.Should().ThrowAsync<AiInvalidOutputException>();
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
