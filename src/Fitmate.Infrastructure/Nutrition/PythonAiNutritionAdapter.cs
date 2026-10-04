using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
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
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(_options.InternalToken))
        {
            throw new AiUnavailableException("AI inference service URL or internal token is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/v1/nutrition/analyze"));
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

            if (response.StatusCode == HttpStatusCode.BadGateway)
                throw new AiInvalidOutputException();
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

            NutritionScanResultView? result;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                result = ReadResult(document.RootElement);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new AiInvalidOutputException();
            }

            ValidateOutput(result);
            return result!;
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

    private static void ValidateOutput(NutritionScanResultView? result)
    {
        var invalid = result is null ||
            result.Items is null || result.Items.Count == 0 || result.Items.Count > 20 ||
            result.TotalCalories < 0 || result.TotalCalories > 10000 ||
            result.Confidence < 0 || result.Confidence > 1 ||
            !string.Equals(result.Source, "ai_estimate", StringComparison.Ordinal) ||
            result.Items.Any(item =>
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 200 ||
                item.Calories < 0 || item.Calories > 10000 ||
                item.ProteinG < 0 || item.ProteinG > 1000 ||
                item.CarbsG < 0 || item.CarbsG > 1000 ||
                item.FatG < 0 || item.FatG > 1000);

        if (invalid)
            throw new AiInvalidOutputException();
    }

    private static NutritionScanResultView ReadResult(JsonElement root)
    {
        if (!root.TryGetProperty("items", out var itemsElement) ||
            itemsElement.ValueKind != JsonValueKind.Array)
        {
            return ReadNutrition5kResult(root);
        }

        var topLevelIngredients = ReadIngredients(root);
        var items = new List<NutritionScanItemView>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            var itemIngredients = ReadIngredients(item);
            var ingredients = itemIngredients.Count > 0 ? itemIngredients : topLevelIngredients;
            items.Add(new NutritionScanItemView(
                RequiredString(item, "name"),
                RoundedInt(item, "calories"),
                RoundedInt(item, "protein_g"),
                RoundedInt(item, "carbs_g"),
                RoundedInt(item, "fat_g"),
                ingredients.Count > 0 ? ingredients : null));
        }

        return new NutritionScanResultView(
            items,
            RoundedInt(root, "total_calories"),
            DecimalNumber(root, "confidence"),
            RequiredString(root, "source"),
            OptionalString(root, "note"));
    }

    private static NutritionScanResultView ReadNutrition5kResult(JsonElement root)
    {
        if (!root.TryGetProperty("ingredients", out var ingredientsElement) ||
            ingredientsElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("AI response is missing items or ingredients.");
        }

        var ingredients = ReadIngredients(root);
        var names = ingredients.Select(ingredient => ingredient.Name).ToList();

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
                    fat,
                    ingredients.Count > 0 ? ingredients : null)
            },
            calories,
            unknown ? 0.2m : 0.9m,
            "ai_estimate",
            "Ket qua uoc tinh, vui long kiem tra truoc khi luu.");
    }

    private static IReadOnlyList<NutritionScanIngredientView> ReadIngredients(JsonElement element)
    {
        if (!element.TryGetProperty("ingredients", out var ingredientsElement) ||
            ingredientsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NutritionScanIngredientView>();
        }

        var ingredients = new List<NutritionScanIngredientView>();
        foreach (var ingredient in ingredientsElement.EnumerateArray())
        {
            var name = ingredient.ValueKind == JsonValueKind.Object
                ? OptionalString(ingredient, "name")
                : ingredient.ValueKind == JsonValueKind.String
                    ? ingredient.GetString()
                    : null;

            name = CleanName(name);
            if (string.IsNullOrWhiteSpace(name) ||
                ingredients.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var portion = ingredient.ValueKind == JsonValueKind.Object
                ? RoundedInt(ingredient, "portion_estimate_g")
                : 0;
            ingredients.Add(new NutritionScanIngredientView(name, portion));
            if (ingredients.Count >= 8)
                break;
        }

        return ingredients;
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
    private static ValidationException InvalidFile(string message) => new(
        ErrorCodes.Validation.FieldInvalid,
        message,
        new Dictionary<string, string[]> { ["file"] = new[] { message } });
}
