using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public sealed record AdminTrainerReview(
    Guid Id, Guid FromUserId, Guid PtUserId, int Rating, string Text, DateTime CreatedAtUtc,
    bool IsHidden, string? ModerationReason, Guid? ModeratedByAdminId, DateTime? ModeratedAtUtc);
public sealed record AdminTrainerReviewPage(
    IReadOnlyList<AdminTrainerReview> Items, int Total, int Page, int PageSize);
public sealed record ModerateTrainerReviewRequest(bool Hidden, string Reason);

public interface IAdminTrainerReviewsStore
{
    Task<AdminTrainerReviewPage> ListAsync(Guid? ptUserId, int? rating, bool? hidden,
        int page, int pageSize, CancellationToken ct);
    Task<AdminTrainerReview> ModerateAsync(Guid reviewId, Guid adminId, bool hidden,
        string reason, CancellationToken ct);
}

public sealed class AdminTrainerReviewsService(IAdminTrainerReviewsStore store, IAuthorizationGuard authz)
{
    public async Task<AdminTrainerReviewPage> ListAsync(Guid adminId, Guid? ptUserId,
        int? rating, bool? hidden, int page, int pageSize, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        if (rating is < 1 or > 5 || page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw new DomainException("REVIEW_FILTER_INVALID", "Check rating and pagination.", 400);
        return await store.ListAsync(ptUserId, rating, hidden, page, pageSize, ct);
    }

    public async Task<AdminTrainerReview> ModerateAsync(Guid adminId, Guid reviewId,
        ModerateTrainerReviewRequest request, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        if (request is null || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length is < 5 or > 500)
            throw new DomainException("REVIEW_REASON_INVALID", "Reason must contain 5 to 500 characters.", 400);
        return await store.ModerateAsync(reviewId, adminId, request.Hidden, request.Reason.Trim(), ct);
    }
}
