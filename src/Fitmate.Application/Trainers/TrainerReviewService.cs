using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Review;

namespace Fitmate.Application.Trainers;

public class TrainerReviewService : ITrainerReviewService
{
    private readonly ITrainerReviewStore _store;
    private readonly IOutboxDispatcher _outbox;

    public TrainerReviewService(ITrainerReviewStore store, IOutboxDispatcher outbox)
    {
        _store = store;
        _outbox = outbox;
    }

    public async Task<ReviewDto> SubmitReviewAsync(Guid reviewerId, Guid ptUserId, CreateReviewRequest request, CancellationToken ct)
    {
        ValidateRequest(request);

        if (reviewerId == ptUserId)
        {
            throw new ConflictException(ErrorCodes.Trainer.CannotSelfReview, "You cannot review yourself.");
        }

        var profile = await _store.FindPtProfileByUserIdAsync(ptUserId, ct);
        if (profile is null)
        {
            throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"Trainer {ptUserId} not found.");
        }

        var hasCompleted = await _store.HasCompletedBookingAsync(reviewerId, ptUserId, ct);
        if (!hasCompleted)
        {
            throw new ForbiddenException(ErrorCodes.Trainer.BookingNotEligible,
                "Only members with at least one completed appointment can review this trainer.");
        }

        var hasExisting = await _store.HasExistingReviewAsync(reviewerId, ptUserId, ct);
        if (hasExisting)
        {
            throw new ConflictException(ErrorCodes.Trainer.AlreadyReviewed,
                "You have already submitted a review for this trainer.");
        }

        var review = Review.Create(reviewerId, ptUserId, request.Rating, request.Text.Trim());
        await _store.AddReviewAsync(review, ct);

        // Update trainer rating aggregates
        var currentCount = profile.ReviewsCount;
        var currentAvg = profile.Rating;
        var newCount = currentCount + 1;
        var newAvg = Math.Round(((currentAvg * currentCount) + request.Rating) / newCount, 1);
        profile.ApplyRating(newAvg, newCount);

        _outbox.Enqueue("trainer.review_submitted", new
        {
            ReviewId = review.Id,
            ReviewerId = reviewerId,
            PtUserId = ptUserId,
            Rating = request.Rating
        });

        await _store.SaveChangesAsync(ct);

        var reviewerName = await _store.GetUserNameAsync(reviewerId, ct);

        return new ReviewDto(
            Id: review.Id,
            FromUserId: review.FromUserId,
            ReviewerName: reviewerName,
            PtUserId: review.PtUserId,
            Rating: review.Rating,
            Text: review.Text,
            CreatedAtUtc: review.CreatedAt);
    }

    public async Task<TrainerReviewsSummaryDto> GetReviewsAsync(Guid ptUserId, int limit, CancellationToken ct)
    {
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var profile = await _store.FindPtProfileByUserIdAsync(ptUserId, ct);
        var reviewsWithAuthor = await _store.GetReviewsForTrainerAsync(ptUserId, clampedLimit, ct);

        var items = reviewsWithAuthor.Select(r => new ReviewDto(
            Id: r.Review.Id,
            FromUserId: r.Review.FromUserId,
            ReviewerName: r.ReviewerName,
            PtUserId: r.Review.PtUserId,
            Rating: r.Review.Rating,
            Text: r.Review.Text,
            CreatedAtUtc: r.Review.CreatedAt)).ToList();

        var avg = profile?.Rating ?? 0m;
        var total = profile?.ReviewsCount ?? 0;

        return new TrainerReviewsSummaryDto(
            AverageRating: avg,
            TotalReviews: total,
            Reviews: items);
    }

    private static void ValidateRequest(CreateReviewRequest req)
    {
        if (req.Rating is < 1 or > 5)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Rating must be between 1 and 5.",
                new Dictionary<string, string[]> { ["rating"] = new[] { "Rating must be between 1 and 5." } });
        }

        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Trim().Length < 3 || req.Text.Trim().Length > 1000)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Review text must be between 3 and 1000 characters.",
                new Dictionary<string, string[]> { ["text"] = new[] { "Review text must be between 3 and 1000 characters." } });
        }
    }
}

