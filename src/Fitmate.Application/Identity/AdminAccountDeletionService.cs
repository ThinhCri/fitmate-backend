using System.Text.Json.Serialization;
using Fitmate.Application.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Identity;

public sealed record AdminDeletionCase(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("fullName")] string? FullName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("requestedAt")] DateTime? RequestedAt,
    [property: JsonPropertyName("deletionStartedAt")] DateTime? DeletionStartedAt,
    [property: JsonPropertyName("deleteAfterUtc")] DateTime? DeleteAfterUtc);

/// <summary>
/// Must remain false until the DB/media scrub worker and its PostgreSQL
/// integration tests are in place. Never start an irreversible countdown
/// without a finalizer that can finish it.
/// </summary>
public sealed record AccountDeletionFinalizationGate(bool Ready);

public sealed class AdminAccountDeletionService(
    IAccountDeletionStore store,
    IRefreshTokenStore refreshTokens,
    IAuthorizationGuard authorization,
    IAuditLogger audit,
    AccountDeletionFinalizationGate finalizationGate)
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);
    public bool CanStartCountdown => finalizationGate.Ready;

    public async Task<(IReadOnlyList<AdminDeletionCase> Items, int Total)> ListAsync(
        Guid adminId, int page, int pageSize, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(adminId, "Admin", ct);
        if (page < 1 || pageSize < 1 || pageSize > 100)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Invalid pagination.",
                new Dictionary<string, string[]> { ["pagination"] = ["page >= 1; pageSize 1..100"] });
        var (items, total) = await store.ListForAdminAsync(page, pageSize, ct);
        return (items.Select(Map).ToList(), total);
    }

    public async Task<AdminDeletionCase> StartCountdownAsync(Guid adminId, Guid userId, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(adminId, "Admin", ct);
        if (!finalizationGate.Ready)
            throw new DomainException("ACCOUNT_DELETION_FINALIZER_NOT_READY",
                "Account deletion cannot start until the permanent data scrub is verified.", 503);
        if (adminId == userId)
            throw new ConflictException("ADMIN_DELETION_SELF", "An admin cannot delete their own account.");
        return await store.WithUserLockAsync(userId, async () =>
        {
            var user = await store.FindIncludingPendingAsync(userId, ct)
                ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, "User not found.");
            if (await store.GetPrimaryRoleAsync(userId, ct) == "Admin")
                throw new ConflictException("ADMIN_DELETION_FORBIDDEN", "Admin accounts require a separate recovery process.");
            if (user.Status != UserStatus.Active || user.DeletionRequestedAt is null)
                throw new ConflictException("DELETION_NOT_REQUESTED", "A verified deletion request is required first.");
            if (await store.HasActiveBookingsAsync(userId, ct))
                throw new ConflictException(ErrorCodes.Identity.HasActiveBookings, "Resolve active bookings before deletion.");

            var now = DateTime.UtcNow;
            user.Status = UserStatus.PendingDeletion;
            user.DeletedAt = now;
            user.UpdatedAt = now;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            await refreshTokens.RevokeAllForUserAsync(userId, ct);
            await store.RevokeUserDevicesAsync(userId, ct);
            audit.Stage("user.deletion_countdown_started", "User", userId, adminId);
            await store.SaveChangesAsync(ct);
            return Map(user);
        }, ct);
    }

    public async Task<AdminDeletionCase> RestoreAsync(Guid adminId, Guid userId, string reason, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(adminId, "Admin", ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Length > 500)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "A reason of 5–500 characters is required.",
                new Dictionary<string, string[]> { ["reason"] = ["5..500 characters"] });
        return await store.WithUserLockAsync(userId, async () =>
        {
            var user = await store.FindIncludingPendingAsync(userId, ct)
                ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, "User not found.");
            if (user.Status != UserStatus.PendingDeletion || user.DeletedAt is null)
                throw new ConflictException("DELETION_NOT_PENDING", "Account is not in the deletion grace period.");
            if (DateTime.UtcNow >= user.DeletedAt.Value.Add(GracePeriod))
                throw new ConflictException("DELETION_GRACE_EXPIRED", "The 30-day restoration period has expired.");

            user.Status = UserStatus.Active;
            user.DeletionRequestedAt = null;
            user.DeletedAt = null;
            user.UpdatedAt = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            audit.Stage("user.deletion_restored", "User", userId, adminId, reason.Trim());
            await store.SaveChangesAsync(ct);
            return Map(user);
        }, ct);
    }

    private static AdminDeletionCase Map(Fitmate.Domain.Identity.ApplicationUser user) =>
        new(user.Id, user.Email, user.FullName,
            user.Status == UserStatus.PendingDeletion ? "pending_deletion" :
                user.DeletionRequestedAt.HasValue ? "requested" : "active",
            user.DeletionRequestedAt, user.DeletedAt,
            user.Status == UserStatus.PendingDeletion ? user.DeletedAt?.Add(GracePeriod) : null);
}
