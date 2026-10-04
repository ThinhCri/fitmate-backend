using System.Net.Http.Headers;
using System.Text.Json;
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

            using var document = JsonDocument.Parse(responseJson);
            return ReadResult(document.RootElement);
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

    private static NutritionScanResultView ReadResult(JsonElement root)
    {
        if (root.TryGetProperty("items", out var itemsElement) &&
            itemsElement.ValueKind == JsonValueKind.Array)
        {
            var items = itemsElement.EnumerateArray()
                .Select(item => new NutritionScanItemView(
                    RequiredString(item, "name"),
                    RoundedInt(item, "calories"),
                    RoundedInt(item, "protein_g"),
                    RoundedInt(item, "carbs_g"),
                    RoundedInt(item, "fat_g")))
                .ToList();

            return new NutritionScanResultView(
                items,
                RoundedInt(root, "total_calories"),
                DecimalNumber(root, "confidence"),
                RequiredString(root, "source"),
                OptionalString(root, "note"));
        }

        if (!root.TryGetProperty("ingredients", out var ingredientsElement) ||
            ingredientsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("AI response is missing items or ingredients.");
        }

        var names = new List<string>();
        foreach (var ingredient in ingredientsElement.EnumerateArray())
        {
            var name = ingredient.ValueKind == JsonValueKind.Object
                ? OptionalString(ingredient, "name")
                : ingredient.ValueKind == JsonValueKind.String
                    ? ingredient.GetString()
                    : null;

            name = CleanName(name);
            if (!string.IsNullOrWhiteSpace(name) &&
                !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
            names.Add("Khong xac dinh");

        var calories = RoundedInt(root, "calories_kcal");
        var protein = RoundedInt(root, "protein_g");
        var carbs = RoundedInt(root, "carbs_g");
        var fat = RoundedInt(root, "fat_g");
        var unknown = calories == 0 && names.Count == 1 &&
            string.Equals(names[0], "Khong xac dinh", StringComparison.OrdinalIgnoreCase);

        return new NutritionScanResultView(
            new[]
            {
                new NutritionScanItemView(
                    string.Join(", ", names.Take(8)),
                    calories,
                    protein,
                    carbs,
                    fat)
            },
            calories,
            unknown ? 0.2m : 0.9m,
            "ai_estimate",
            "Ket qua uoc tinh, vui long kiem tra truoc khi luu.");
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"AI response field '{name}' is invalid.");
        }

        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new JsonException($"AI response field '{name}' is invalid.");
        return value.GetString();
    }

    private static int RoundedInt(JsonElement element, string name)
    {
        var value = DecimalNumber(element, name);
        if (value < int.MinValue || value > int.MaxValue)
            throw new JsonException($"AI response field '{name}' is out of range.");
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static decimal DecimalNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var number))
        {
            throw new JsonException($"AI response field '{name}' is invalid.");
        }

        return number;
    }

    private static string CleanName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Replace("\\", " ", StringComparison.Ordinal)
            .Trim(' ', '.', ',', ';', ':', '-', '_');

        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);

        return text.Length <= 200 ? text : text[..200];
    }
}
