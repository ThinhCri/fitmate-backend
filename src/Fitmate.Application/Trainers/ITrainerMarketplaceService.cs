using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

public enum CertificateVerifiedFilter { Any, VerifiedOnly }

public sealed record TrainerMarketplaceQuery(
    string? Specialty,
    long? MinPriceVnd,
    long? MaxPriceVnd,
    decimal? MinRating,
    int Limit,
    string? Cursor);

public sealed record TrainerMarketplaceSummary(
    Guid Id,
    Guid UserId,
    string? FullName,
    string? AvatarUrl,
    string? Specialty,
    int? ExperienceYears,
    long? PricePerSessionVnd,
    decimal Rating,
    int ReviewsCount,
    bool IsVerified);

public sealed record TrainerMarketplaceDetail(
    TrainerMarketplaceSummary Profile,
    string? Bio,
    IReadOnlyList<Domain.Trainers.Service> Services);

public interface ITrainerMarketplaceService
{
    Task<(IReadOnlyList<TrainerMarketplaceSummary> Items, string? NextCursor)> ListAsync(TrainerMarketplaceQuery q, CancellationToken ct);
    Task<TrainerMarketplaceDetail> GetDetailAsync(Guid ptUserId, CancellationToken ct);
}
