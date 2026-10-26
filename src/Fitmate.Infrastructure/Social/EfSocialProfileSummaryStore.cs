using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Social;

public sealed class EfSocialProfileSummaryStore(FitmateDbContext db) : ISocialProfileSummaryStore
{
    public async Task<(int VisiblePosts, int Friends, int? OwnWorkouts)> ReadAsync(Guid viewerId, Guid targetId, CancellationToken ct)
    {
        var friends = await db.Friendships.CountAsync(f => f.UserLowId == targetId || f.UserHighId == targetId, ct);
        var maySeeFriends = viewerId == targetId || await db.Friendships.AnyAsync(f =>
            (f.UserLowId == viewerId && f.UserHighId == targetId) || (f.UserLowId == targetId && f.UserHighId == viewerId), ct);
        var posts = await db.Posts.CountAsync(p => p.AuthorId == targetId && p.DeletedAt == null &&
            (p.Visibility == PostVisibility.Public || viewerId == targetId || (p.Visibility == PostVisibility.Friends && maySeeFriends)), ct);
        int? workouts = viewerId == targetId
            ? await db.WorkoutSessions.CountAsync(w => w.UserId == viewerId && w.DeletedAt == null && w.Status == WorkoutSessionStatus.Completed, ct)
            : null;
        return (posts, friends, workouts);
    }
}
