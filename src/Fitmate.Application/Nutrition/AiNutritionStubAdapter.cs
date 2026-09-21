using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;

namespace Fitmate.Application.Nutrition;

public class AiNutritionStubAdapter : IAiNutritionAdapter
{
    public Task<NutritionScanResultView> EstimateFromImageAsync(
        Stream imageStream,
        string contentType,
        CancellationToken ct)
    {
        throw new DomainException(
            ErrorCodes.AI.Unavailable,
            "AI meal scan is not available. Provider not configured.");
    }
}
