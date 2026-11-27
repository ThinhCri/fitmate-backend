using Fitmate.Application.Trainers;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Review;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Trainers;

public class EfTrainerReviewStore : ITrainerReviewStore
{
    private readonly FitmateDbContext _db;

    public EfTrainerReviewStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasCompletedBookingAsync(Guid memberId, Guid ptUserId, CancellationToken ct)
    {
        return await _db.Appointments.AnyAsync(a =>
            a.MemberId == memberId &&
            a.PtUserId == ptUserId &&
            a.Status == AppointmentStatus.Completed,
            ct);
    }

    public async Task<bool> HasExistingReviewAsync(Guid memberId, Guid ptUserId, CancellationToken ct)
    {
        return await _db.Reviews.AnyAsync(r =>
            r.FromUserId == memberId &&
            r.PtUserId == ptUserId,
            ct);
    }

    public async Task AddReviewAsync(Review review, CancellationToken ct)
    {
        await _db.Reviews.AddAsync(review, ct);
    }

    public async Task<IReadOnlyList<(Review Review, string ReviewerName)>> GetReviewsForTrainerAsync(Guid ptUserId, int limit, CancellationToken ct)
    {
        var rows = await (
            from r in _db.Reviews
            join u in _db.Users on r.FromUserId equals u.Id into users
            from u in users.DefaultIfEmpty()
            where r.PtUserId == ptUserId && !r.IsHidden
            orderby r.CreatedAt descending
            select new
            {
                Review = r,
                ReviewerName = u != null && !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : "Học viên Fitmate"
            }
        ).Take(limit).ToListAsync(ct);

        return rows.Select(x => (x.Review, x.ReviewerName)).ToList();
    }

    public async Task<PtProfile?> FindPtProfileByUserIdAsync(Guid ptUserId, CancellationToken ct)
    {
        return await _db.PtProfiles.FirstOrDefaultAsync(p => p.UserId == ptUserId, ct);
    }

    public async Task<(decimal AverageRating, int TotalReviews)> ComputeRatingStatsAsync(Guid ptUserId, CancellationToken ct)
    {
        var count = await _db.Reviews.CountAsync(r => r.PtUserId == ptUserId && !r.IsHidden, ct);
        if (count == 0) return (0m, 0);

        var avg = (decimal)await _db.Reviews
            .Where(r => r.PtUserId == ptUserId && !r.IsHidden)
            .AverageAsync(r => r.Rating, ct);

        return (Math.Round(avg, 1), count);
    }

    public async Task<string> GetUserNameAsync(Guid userId, CancellationToken ct)
    {
        var name = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(ct);

        return name ?? "Anonymous";
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}
