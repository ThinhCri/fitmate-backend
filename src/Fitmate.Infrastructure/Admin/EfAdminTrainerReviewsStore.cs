using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Admin;

public sealed class EfAdminTrainerReviewsStore(FitmateDbContext db) : IAdminTrainerReviewsStore
{
    public async Task<AdminTrainerReviewPage> ListAsync(Guid? ptUserId, int? rating, bool? hidden,
        int page, int pageSize, CancellationToken ct)
    {
        var query = db.Reviews.AsNoTracking();
        if (ptUserId.HasValue) query = query.Where(r => r.PtUserId == ptUserId.Value);
        if (rating.HasValue) query = query.Where(r => r.Rating == rating.Value);
        if (hidden.HasValue) query = query.Where(r => r.IsHidden == hidden.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new AdminTrainerReview(r.Id, r.FromUserId, r.PtUserId,
                r.Rating, r.Text, r.CreatedAt, r.IsHidden, r.ModerationReason,
                r.ModeratedByAdminId, r.ModeratedAtUtc)).ToListAsync(ct);
        return new AdminTrainerReviewPage(items, total, page, pageSize);
    }

    public async Task<AdminTrainerReview> ModerateAsync(Guid reviewId, Guid adminId, bool hidden,
        string reason, CancellationToken ct)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId, ct)
            ?? throw new NotFoundException("REVIEW_NOT_FOUND", "Review not found.");
        if (review.IsHidden == hidden)
            throw new ConflictException("REVIEW_STATE_UNCHANGED", "Review already has this visibility.");
        review.SetHidden(hidden, adminId, reason, DateTime.UtcNow);
        var profile = await db.PtProfiles.SingleAsync(p => p.UserId == review.PtUserId, ct);
        var otherVisible = db.Reviews.Where(r => r.PtUserId == review.PtUserId &&
            r.Id != review.Id && !r.IsHidden);
        var otherCount = await otherVisible.CountAsync(ct);
        var otherRatingSum = await otherVisible.SumAsync(r => (int?)r.Rating, ct) ?? 0;
        var count = otherCount + (hidden ? 0 : 1);
        var average = count == 0 ? 0m : Math.Round(
            (decimal)(otherRatingSum + (hidden ? 0 : review.Rating)) / count,
            1, MidpointRounding.AwayFromZero);
        profile.ApplyRating(average, count);
        db.AuditLogs.Add(AuditLog.Record(adminId, "Admin",
            hidden ? "trainer_review.hidden" : "trainer_review.restored",
            "trainer_review", review.Id, reason));
        await db.SaveChangesAsync(ct);
        return new AdminTrainerReview(review.Id, review.FromUserId, review.PtUserId,
            review.Rating, review.Text, review.CreatedAt, review.IsHidden,
            review.ModerationReason, review.ModeratedByAdminId, review.ModeratedAtUtc);
    }
}
