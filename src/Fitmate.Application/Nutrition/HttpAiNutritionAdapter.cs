using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Nutrition;

public class HttpAiNutritionAdapter : IAiNutritionAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<AiOptions> _options;
    private readonly ILogger<HttpAiNutritionAdapter> _logger;

    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    public HttpAiNutritionAdapter(
        HttpClient httpClient,
        IOptions<AiOptions> options,
        ILogger<HttpAiNutritionAdapter> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<NutritionScanResultView> EstimateFromImageAsync(
        Stream imageStream,
        string contentType,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (imageStream == null || (imageStream.CanSeek && imageStream.Length == 0))
        {
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Image content cannot be empty.",
                new Dictionary<string, string[]> { ["image"] = new[] { "Image stream is empty or null." } });
        }

        if (string.IsNullOrWhiteSpace(contentType) || !SupportedContentTypes.Contains(contentType.Trim()))
        {
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                $"Unsupported content type '{contentType}'. Supported types: image/jpeg, image/png, image/webp.",
                new Dictionary<string, string[]> { ["contentType"] = new[] { "Must be image/jpeg, image/png, or image/webp." } });
        }

        var opt = _options.Value;
        if (!opt.Enabled || !opt.FoodScanEnabled)
        {
            throw new AiUnavailableException("AI meal scan is not available. Provider not configured or disabled.");
        }

        var apiUrl = opt.ModelPath ?? "http://localhost:8000/predict";

        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(imageStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", "image.jpg");

        try
        {
            var response = await _httpClient.PostAsync(apiUrl, content, ct);

            var responseJson = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "AI Server returned {StatusCode} at {Url}. Body: {Body}",
                    (int)response.StatusCode, apiUrl, responseJson);
                throw new AiUnavailableException(
                    $"AI server returned HTTP {(int)response.StatusCode}: {responseJson}");
            }

            _logger.LogInformation("AI Server response: {Body}", responseJson);

            var result = JsonSerializer.Deserialize<QwenPredictionResponse>(responseJson);

            if (result == null)
            {
                throw new AiUnavailableException("Invalid response from AI server.");
            }

            var itemName = result.Ingredients != null && result.Ingredients.Count > 0 
                ? string.Join(", ", result.Ingredients) 
                : "Unknown Food";

            var item = new NutritionScanItemView(
                Name: itemName,
                Calories: (int)result.CaloriesKcal,
                ProteinG: (int)result.ProteinG,
                CarbsG: (int)result.CarbsG,
                FatG: (int)result.FatG
            );

            return new NutritionScanResultView(
                Items: new List<NutritionScanItemView> { item },
                TotalCalories: (int)result.CaloriesKcal,
                Confidence: 0.9m,
                Source: "ai_estimate",
                Note: $"Portion estimate: {result.PortionEstimateG}g"
            );
        }
        catch (AiUnavailableException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AI Server request timed out at {Url}", apiUrl);
            throw new AiUnavailableException("AI server request timed out. The model may need more time to process the image.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call AI Server at {Url}", apiUrl);
            throw new AiUnavailableException("Error communicating with AI service: " + ex.Message);
        }
    }

    private class QwenPredictionResponse
    {
        [JsonPropertyName("ingredients")]
        public List<string>? Ingredients { get; set; }

        [JsonPropertyName("portion_estimate_g")]
        public decimal PortionEstimateG { get; set; }

        [JsonPropertyName("calories_kcal")]
        public decimal CaloriesKcal { get; set; }

        [JsonPropertyName("protein_g")]
        public decimal ProteinG { get; set; }

        [JsonPropertyName("carbs_g")]
        public decimal CarbsG { get; set; }

        [JsonPropertyName("fat_g")]
        public decimal FatG { get; set; }
    }
}
