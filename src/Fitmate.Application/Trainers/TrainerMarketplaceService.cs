using Fitmate.Application.Common;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Default <see cref="ITrainerMarketplaceService"/> impl. Endpoint public (theo OpenAPI <c>/trainers</c>).
/// Verified-only filter: PT chỉ xuất hiện nếu có ít nhất 1 Certificate.Status = Approved.
/// </summary>
public class TrainerMarketplaceService : ITrainerMarketplaceService
{
    private readonly ITrainerMarketplaceStore _store;

    public TrainerMarketplaceService(ITrainerMarketplaceStore store)
    {
        _store = store;
    }

    public async Task<(IReadOnlyList<TrainerMarketplaceSummary> Items, string? NextCursor)> ListAsync(TrainerMarketplaceQuery q, CancellationToken ct)
    {
        Validate(q);
        if (q.MinPriceVnd.HasValue && q.MaxPriceVnd.HasValue && q.MaxPriceVnd < q.MinPriceVnd)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "maxPriceVnd < minPriceVnd",
                new Dictionary<string, string[]> { ["maxPriceVnd"] = new[] { ">= minPriceVnd" } });
        return await _store.ListVerifiedAsync(q, ct);
    }

    public async Task<TrainerMarketplaceDetail> GetDetailAsync(Guid ptUserId, CancellationToken ct)
    {
        var detail = await _store.FindDetailAsync(ptUserId, ct);
        if (detail is null)
            throw new NotFoundException(ErrorCodes.General.NotFound, $"Trainer {ptUserId} not found or not verified.");
        if (!detail.Profile.IsVerified)
            throw new NotFoundException(ErrorCodes.General.NotFound, $"Trainer {ptUserId} is not verified.");
        return detail;
    }

    private static void Validate(TrainerMarketplaceQuery q)
    {
        if (q.Limit is < 1 or > 100)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "limit 1..100",
                new Dictionary<string, string[]> { ["limit"] = new[] { "1..100" } });
        if (q.MinRating is < 0m or > 5m)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "minRating 0..5",
                new Dictionary<string, string[]> { ["minRating"] = new[] { "0..5" } });
        if (q.Specialty is { Length: > 200 })
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "specialty max 200 chars",
                new Dictionary<string, string[]> { ["specialty"] = new[] { "max 200" } });
    }
}
