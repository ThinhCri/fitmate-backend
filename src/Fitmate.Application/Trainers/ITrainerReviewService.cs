using Fitmate.Domain.Review;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

public interface ITrainerReviewService
{
    Task<ReviewDto> SubmitReviewAsync(Guid reviewerId, Guid ptUserId, CreateReviewRequest request, CancellationToken ct);
    Task<TrainerReviewsSummaryDto> GetReviewsAsync(Guid ptUserId, int limit, CancellationToken ct);
}

public interface ITrainerReviewStore
{
    Task<bool> HasCompletedBookingAsync(Guid memberId, Guid ptUserId, CancellationToken ct);
    Task<bool> HasExistingReviewAsync(Guid memberId, Guid ptUserId, CancellationToken ct);
    Task AddReviewAsync(Review review, CancellationToken ct);
    Task<IReadOnlyList<(Review Review, string ReviewerName)>> GetReviewsForTrainerAsync(Guid ptUserId, int limit, CancellationToken ct);
    Task<PtProfile?> FindPtProfileByUserIdAsync(Guid ptUserId, CancellationToken ct);
    Task<(decimal AverageRating, int TotalReviews)> ComputeRatingStatsAsync(Guid ptUserId, CancellationToken ct);
    Task<string> GetUserNameAsync(Guid userId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

