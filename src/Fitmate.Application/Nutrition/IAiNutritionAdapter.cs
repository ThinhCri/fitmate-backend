using Fitmate.Domain.Common;

namespace Fitmate.Application.Nutrition;

public interface IAiNutritionAdapter
{
    Task<NutritionScanResultView> EstimateFromImageAsync(
        Stream imageStream,
        string contentType,
        CancellationToken ct);
}
