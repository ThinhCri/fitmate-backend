using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Nutrition;

/// <summary>
/// Fail-closed unavailable adapter when no real provider is selected.
/// Validates input, cancellation, and config gating; throws typed 503 AiUnavailableException.
/// Real inference adapters are selected in the API composition root.
/// </summary>
public class UnavailableNutritionAdapter : IAiNutritionAdapter
{
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp"
    };

    private readonly IOptions<AiOptions>? _options;

    public UnavailableNutritionAdapter(IOptions<AiOptions>? options = null)
    {
        _options = options;
    }

    public Task<NutritionScanResultView> EstimateFromImageAsync(
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
                new Dictionary<string, string[]>
                {
                    ["image"] = new[] { "Image stream is empty or null." }
                });
        }

        if (string.IsNullOrWhiteSpace(contentType) || !SupportedContentTypes.Contains(contentType.Trim()))
        {
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                $"Unsupported content type '{contentType}'. Supported types: image/jpeg, image/png, image/webp.",
                new Dictionary<string, string[]>
                {
                    ["contentType"] = new[] { "Must be image/jpeg, image/png, or image/webp." }
                });
        }

        var opt = _options?.Value;
        if (opt is not null && opt.Enabled && opt.FoodScanEnabled)
        {
            // When real model is integrated in later phases of BE-817, inference execution will occur here.
            // Currently awaiting artifact handoff per ai-contract.md.
            throw new AiUnavailableException("AI meal scan provider is not configured.");
        }

        throw new AiUnavailableException("AI meal scan is not available. Provider not configured or disabled.");
    }
}
