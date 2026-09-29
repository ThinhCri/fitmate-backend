using Fitmate.Application.Auth;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Admin;

public record AuditItem(Guid Id, Guid? ActorId, string Action, string TargetType, Guid? TargetId, string? Reason, DateTime CreatedAt);
public record AuditPage(IReadOnlyList<AuditItem> Items, int Total, int Page, int PageSize);
public record BookingPaymentItem(Guid Id, string Status, string Method, long AmountVnd, long RefundedVnd, DateTime CreatedAt);
public record BookingRefundItem(Guid Id, Guid PaymentId, string Status, long AmountVnd, string Reason, string? TransferReference, DateTime CreatedAt);
public record BookingReview(Guid Id, string AppointmentStatus, IReadOnlyList<BookingPaymentItem> Payments, IReadOnlyList<BookingRefundItem> Refunds);
public record TrainerReview(string? Specialty, int? ExperienceYears, string? Bio);
public interface IAdminReviewStore
{
    Task<AuditPage> AuditAsync(Guid? targetId, string? action, int page, int pageSize, CancellationToken ct);
    Task<BookingReview?> BookingAsync(Guid id, CancellationToken ct);
    Task<TrainerReview?> TrainerAsync(Guid id, CancellationToken ct);
}
public sealed class AdminReviewService(IAdminReviewStore store, IAuthorizationGuard authz)
{
    public async Task<AuditPage> AuditAsync(Guid adminId, Guid? targetId, string? action, int page, int pageSize, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        if (page < 1 || page > 100000 || pageSize < 1 || pageSize > 100 || action?.Length > 100)
            throw new DomainException("ADMIN_QUERY_INVALID", "Invalid pagination or action filter.", 400);
        return await store.AuditAsync(targetId, action, page, pageSize, ct);
    }
    public async Task<BookingReview> BookingAsync(Guid adminId, Guid id, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        return await store.BookingAsync(id, ct) ?? throw new NotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
    }
    public async Task<TrainerReview?> TrainerAsync(Guid adminId, Guid id, CancellationToken ct)
    {
        await authz.EnsureRoleAsync(adminId, "Admin", ct);
        return await store.TrainerAsync(id, ct);
    }
}
