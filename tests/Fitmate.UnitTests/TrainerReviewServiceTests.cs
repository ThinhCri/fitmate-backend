using Fitmate.Application.Outbox;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Review;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class TrainerReviewServiceTests
{
    [Fact]
    public async Task SubmitReviewAsync_CreatesReview_AndUpdatesRatingAggregates()
    {
        var store = new InMemoryTrainerReviewStore();
        var outbox = new InMemoryOutboxDispatcher();

        var reviewerId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();

        var profile = PtProfile.CreateDraft(ptUserId, "Fitness", 5, 500000, "Bio");
        store.Profiles[ptUserId] = profile;
        store.UserNames[reviewerId] = "Tran Van C";
        store.CompletedBookings.Add((reviewerId, ptUserId));

        var svc = new TrainerReviewService(store, outbox);
        var req = new CreateReviewRequest(Rating: 5, Text: "Great trainer, very dedicated!");

        var review = await svc.SubmitReviewAsync(reviewerId, ptUserId, req, CancellationToken.None);

        review.Should().NotBeNull();
        review.Rating.Should().Be(5);
        review.ReviewerName.Should().Be("Tran Van C");
        review.Text.Should().Be("Great trainer, very dedicated!");

        profile.Rating.Should().Be(5.0m);
        profile.ReviewsCount.Should().Be(1);
        outbox.EnqueuedTypes.Should().Contain("trainer.review_submitted");
    }

    [Fact]
    public async Task SubmitReviewAsync_ThrowsConflict_WhenReviewingSelf()
    {
        var store = new InMemoryTrainerReviewStore();
        var outbox = new InMemoryOutboxDispatcher();
        var ptUserId = Guid.NewGuid();

        var svc = new TrainerReviewService(store, outbox);
        var act = () => svc.SubmitReviewAsync(ptUserId, ptUserId, new CreateReviewRequest(5, "Self review"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be(ErrorCodes.Trainer.CannotSelfReview);
    }

    [Fact]
    public async Task SubmitReviewAsync_ThrowsForbidden_WhenNoCompletedBooking()
    {
        var store = new InMemoryTrainerReviewStore();
        var outbox = new InMemoryOutboxDispatcher();

        var reviewerId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();

        var profile = PtProfile.CreateDraft(ptUserId, "Fitness", 5, 500000, "Bio");
        store.Profiles[ptUserId] = profile;

        var svc = new TrainerReviewService(store, outbox);
        var act = () => svc.SubmitReviewAsync(reviewerId, ptUserId, new CreateReviewRequest(4, "Nice session"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.Code.Should().Be(ErrorCodes.Trainer.BookingNotEligible);
    }

    [Fact]
    public async Task SubmitReviewAsync_ThrowsConflict_WhenAlreadyReviewed()
    {
        var store = new InMemoryTrainerReviewStore();
        var outbox = new InMemoryOutboxDispatcher();

        var reviewerId = Guid.NewGuid();
        var ptUserId = Guid.NewGuid();

        var profile = PtProfile.CreateDraft(ptUserId, "Fitness", 5, 500000, "Bio");
        store.Profiles[ptUserId] = profile;
        store.CompletedBookings.Add((reviewerId, ptUserId));
        store.ExistingReviews.Add(Review.Create(reviewerId, ptUserId, 5, "First review"));

        var svc = new TrainerReviewService(store, outbox);
        var act = () => svc.SubmitReviewAsync(reviewerId, ptUserId, new CreateReviewRequest(5, "Second review"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be(ErrorCodes.Trainer.AlreadyReviewed);
    }

    [Fact]
    public async Task SubmitReviewAsync_ThrowsValidation_WhenRatingInvalid()
    {
        var store = new InMemoryTrainerReviewStore();
        var outbox = new InMemoryOutboxDispatcher();
        var svc = new TrainerReviewService(store, outbox);

        var act = () => svc.SubmitReviewAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateReviewRequest(0, "Too low"), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();

        var act2 = () => svc.SubmitReviewAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateReviewRequest(6, "Too high"), CancellationToken.None);
        await act2.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetReviewsAsync_ReturnsAggregatesAndList()
    {
        var store = new InMemoryTrainerReviewStore();
        var outbox = new InMemoryOutboxDispatcher();

        var ptUserId = Guid.NewGuid();
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();

        var profile = PtProfile.CreateDraft(ptUserId, "Fitness", 5, 500000, "Bio");
        profile.ApplyRating(4.5m, 2);
        store.Profiles[ptUserId] = profile;

        store.UserNames[r1] = "Alice";
        store.UserNames[r2] = "Bob";
        store.ExistingReviews.Add(Review.Create(r1, ptUserId, 5, "Awesome"));
        store.ExistingReviews.Add(Review.Create(r2, ptUserId, 4, "Good"));

        var svc = new TrainerReviewService(store, outbox);
        var result = await svc.GetReviewsAsync(ptUserId, 10, CancellationToken.None);

        result.AverageRating.Should().Be(4.5m);
        result.TotalReviews.Should().Be(2);
        result.Reviews.Should().HaveCount(2);
    }
}

internal class InMemoryTrainerReviewStore : ITrainerReviewStore
{
    public Dictionary<Guid, PtProfile> Profiles { get; } = new();
    public HashSet<(Guid MemberId, Guid PtUserId)> CompletedBookings { get; } = new();
    public List<Review> ExistingReviews { get; } = new();
    public Dictionary<Guid, string> UserNames { get; } = new();

    public Task<bool> HasCompletedBookingAsync(Guid memberId, Guid ptUserId, CancellationToken ct)
    {
        return Task.FromResult(CompletedBookings.Contains((memberId, ptUserId)));
    }

    public Task<bool> HasExistingReviewAsync(Guid memberId, Guid ptUserId, CancellationToken ct)
    {
        return Task.FromResult(ExistingReviews.Any(r => r.FromUserId == memberId && r.PtUserId == ptUserId));
    }

    public Task AddReviewAsync(Review review, CancellationToken ct)
    {
        ExistingReviews.Add(review);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(Review Review, string ReviewerName)>> GetReviewsForTrainerAsync(Guid ptUserId, int limit, CancellationToken ct)
    {
        var items = ExistingReviews
            .Where(r => r.PtUserId == ptUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .Select(r => (Review: r, ReviewerName: UserNames.TryGetValue(r.FromUserId, out var name) ? name : "Anonymous"))
            .ToList();

        return Task.FromResult<IReadOnlyList<(Review Review, string ReviewerName)>>(items);
    }

    public Task<PtProfile?> FindPtProfileByUserIdAsync(Guid ptUserId, CancellationToken ct)
    {
        return Task.FromResult(Profiles.TryGetValue(ptUserId, out var p) ? p : null);
    }

    public Task<(decimal AverageRating, int TotalReviews)> ComputeRatingStatsAsync(Guid ptUserId, CancellationToken ct)
    {
        var matching = ExistingReviews.Where(r => r.PtUserId == ptUserId).ToList();
        if (matching.Count == 0) return Task.FromResult((0m, 0));

        var avg = Math.Round((decimal)matching.Average(r => r.Rating), 1);
        return Task.FromResult((avg, matching.Count));
    }

    public Task<string> GetUserNameAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(UserNames.TryGetValue(userId, out var name) ? name : "Anonymous");
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

