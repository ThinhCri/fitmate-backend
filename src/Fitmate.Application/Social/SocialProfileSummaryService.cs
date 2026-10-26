using System.Text.Json.Serialization;
using Fitmate.Application.Identity;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Social;

public sealed record SocialBadge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description);
public sealed record SocialProfileSummary(
    [property: JsonPropertyName("post_count")] int PostCount,
    [property: JsonPropertyName("friend_count")] int FriendCount,
    [property: JsonPropertyName("completed_workouts_count")] int? CompletedWorkoutsCount,
    [property: JsonPropertyName("badges")] IReadOnlyList<SocialBadge> Badges);
public interface ISocialProfileSummaryStore
{
    Task<(int VisiblePosts, int Friends, int? OwnWorkouts)> ReadAsync(Guid viewerId, Guid targetId, CancellationToken ct);
}
public sealed class SocialProfileSummaryService(IUserProfileService profiles, IBlockStore blocks, ISocialProfileSummaryStore store)
{
    public async Task<SocialProfileSummary> GetAsync(Guid viewerId, Guid targetId, CancellationToken ct)
    {
        if (viewerId == Guid.Empty) throw new UnauthorizedException(ErrorCodes.Identity.NotAuthenticated, "Authentication required.");
        if (targetId == Guid.Empty) throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Invalid profile id.", new Dictionary<string, string[]>());
        if (viewerId != targetId && await blocks.IsBlockedEitherWayAsync(viewerId, targetId, ct))
            throw new NotFoundException(ErrorCodes.Identity.UserNotFound, "Profile not found.");
        await profiles.GetPublicProfileAsync(targetId, ct);
        var counts = await store.ReadAsync(viewerId, targetId, ct);
        var badges = new List<SocialBadge>();
        // Derived current activity only: no points, cash, inferred fitness streak, or private health badge.
        if (counts.VisiblePosts >= 10) badges.Add(new("community-ten-visible", "Cộng đồng năng nổ", "Có ít nhất 10 bài viết hiện được phép hiển thị cho bạn."));
        return new SocialProfileSummary(counts.VisiblePosts, counts.Friends, viewerId == targetId ? counts.OwnWorkouts : null, badges);
    }
}
