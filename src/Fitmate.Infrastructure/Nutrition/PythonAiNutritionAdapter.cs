using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Fitmate.Application.Ai;
using Fitmate.Application.Nutrition;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Nutrition;

/// <summary>
/// Calls the separately deployed Python inference service. The public mobile
/// contract remains owned by the .NET API; the internal service is never
/// called directly by a client.
/// </summary>
public sealed class PythonAiNutritionAdapter : IAiNutritionAdapter
{
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public PythonAiNutritionAdapter(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<NutritionScanResultView> EstimateFromImageAsync(
        Stream imageStream,
        string contentType,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateInput(imageStream, contentType);

        if (!_options.Enabled || !_options.FoodScanEnabled ||
            !string.Equals(_options.Provider, "python_service", StringComparison.OrdinalIgnoreCase))
        {
            throw new AiUnavailableException("AI meal scan is disabled or its provider is not configured.");
        }

        if (!Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AiUnavailableException("AI inference service URL is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/predict"));
        if (!string.IsNullOrWhiteSpace(_options.InternalToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.InternalToken.Trim());

        using var multipart = new MultipartFormDataContent();
        using var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType.Trim());
        multipart.Add(imageContent, "file", "meal-image");
        request.Content = multipart;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("AI inference service timed out.");
        }
        catch (HttpRequestException)
        {
            throw new AiUnavailableException("AI inference service could not be reached.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiRateLimitedException();

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity ||
                response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "The AI service could not process this image.",
                    new Dictionary<string, string[]> { ["file"] = new[] { "unsupported or invalid image" } });
            }

            if (!response.IsSuccessStatusCode)
                throw new AiUnavailableException("AI inference service returned an unavailable response.");

            QwenFoodPrediction? prediction;
            try
            {
                prediction = await response.Content.ReadFromJsonAsync<QwenFoodPrediction>(cancellationToken: ct);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new AiInvalidOutputException();
            }

            ValidateOutput(prediction);
            return MapPrediction(prediction!);
        }
    }

    private void ValidateInput(Stream imageStream, string contentType)
    {
        if (imageStream is null || (imageStream.CanSeek && imageStream.Length == 0))
            throw InvalidFile("Image content cannot be empty.");

        if (string.IsNullOrWhiteSpace(contentType) || !SupportedContentTypes.Contains(contentType.Trim()))
            throw InvalidFile("Image must be JPEG, PNG, or WebP.");

        if (imageStream.CanSeek && imageStream.Length > _options.MaxImageSizeBytes)
            throw InvalidFile("Image exceeds the configured size limit.");
    }

    private static void ValidateOutput(QwenFoodPrediction? result)
    {
        var invalid = result is null ||
            result.Ingredients is null || result.Ingredients.Count == 0 || result.Ingredients.Count > 20 ||
            result.Ingredients.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 200) ||
            result.PortionEstimateG < 0 || result.PortionEstimateG > 10000 ||
            result.CaloriesKcal < 0 || result.CaloriesKcal > 10000 ||
            result.ProteinG < 0 || result.ProteinG > 1000 ||
            result.CarbsG < 0 || result.CarbsG > 1000 ||
            result.FatG < 0 || result.FatG > 1000;

        if (invalid)
            throw new AiInvalidOutputException();
    }

    private static NutritionScanResultView MapPrediction(QwenFoodPrediction result)
    {
        var ingredients = result.Ingredients
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var calories = Round(result.CaloriesKcal);
        return new NutritionScanResultView(
            new[] { new NutritionScanItemView("Bữa ăn đã nhận diện", calories, Round(result.ProteinG), Round(result.CarbsG), Round(result.FatG)) },
            calories,
            null,
            "ai_estimate",
            $"AI ước tính khẩu phần khoảng {Round(result.PortionEstimateG)} g. Hãy kiểm tra trước khi lưu.")
        {
            Ingredients = ingredients
        };
    }

    private static int Round(decimal value) => checked((int)Math.Round(value, 0, MidpointRounding.AwayFromZero));

    private sealed record QwenFoodPrediction(
        [property: JsonPropertyName("ingredients")] IReadOnlyList<string> Ingredients,
        [property: JsonPropertyName("portion_estimate_g")] decimal PortionEstimateG,
        [property: JsonPropertyName("calories_kcal")] decimal CaloriesKcal,
        [property: JsonPropertyName("protein_g")] decimal ProteinG,
        [property: JsonPropertyName("carbs_g")] decimal CarbsG,
        [property: JsonPropertyName("fat_g")] decimal FatG);

    private static ValidationException InvalidFile(string message) => new(
        ErrorCodes.Validation.FieldInvalid,
        message,
        new Dictionary<string, string[]> { ["file"] = new[] { message } });
}
