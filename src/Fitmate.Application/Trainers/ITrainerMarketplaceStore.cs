using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>Application abstraction cho trainer marketplace read-side.</summary>
public interface ITrainerMarketplaceStore
{
    Task<(IReadOnlyList<TrainerMarketplaceSummary> Items, string? NextCursor)> ListVerifiedAsync(TrainerMarketplaceQuery q, CancellationToken ct);
    Task<TrainerMarketplaceDetail?> FindDetailAsync(Guid ptUserId, CancellationToken ct);
}
