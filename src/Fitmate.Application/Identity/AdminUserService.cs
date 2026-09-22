using Fitmate.Application.Auth;
using Fitmate.Application.Identity;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Identity;

public class AdminUserService : IAdminUserService
{
    private readonly IAdminUserStore _store;
    private readonly IAuditLogger _audit;
    private readonly IAuthorizationGuard _authz;

    public AdminUserService(IAdminUserStore store, IAuditLogger audit, IAuthorizationGuard authz)
    {
        _store = store;
        _audit = audit;
        _authz = authz;
    }

    public Task<(IReadOnlyList<AdminUserSummary> Items, int Total)> ListAsync(AdminUserListQuery query, CancellationToken ct)
    {
        ValidateQuery(query);
        return _store.ListPagedAsync(query, ct);
    }

    public async Task<AdminUserSummary> GetAsync(Guid userId, CancellationToken ct)
    {
        var summary = await _store.FindSummaryAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");
        return summary;
    }

    public async Task<AdminUserSummary> SetLockAsync(Guid targetUserId, Guid adminUserId, bool locked, string reason, CancellationToken ct)
    {
        // BE-090: enforce role Admin ở Application.
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });
        if (reason.Length > 500)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason tối đa 500 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "max 500 chars" } });

        if (targetUserId == adminUserId)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Admin không thể tự khóa chính mình ở endpoint này.",
                new Dictionary<string, string[]> { ["targetUserId"] = new[] { "self" } });

        var existing = await _store.FindSummaryAsync(targetUserId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {targetUserId} not found.");

        if (locked && string.Equals(existing.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            var activeAdmins = await _store.CountActiveAdminsAsync(ct);
            if (activeAdmins <= 1)
            {
                throw new ConflictException(
                    ErrorCodes.Identity.CannotLockLastAdmin,
                    "Không thể khóa quản trị viên cuối cùng đang hoạt động.");
            }
        }

        DateTimeOffset? until = locked ? DateTimeOffset.UtcNow.AddYears(100) : null;
        var ok = await _store.SetLockAsync(targetUserId, until, ct);
        if (!ok)
            throw new ConflictException(ErrorCodes.General.Internal, "Lock operation failed.");

        // Bump SecurityStamp → invalidate mọi JWT/refresh token của user (session revoke).
        // Khi locked=true, BẮT BUỘC; khi unlock=false vẫn bump để chống stale tokens.
        await _store.UpdateSecurityStampAsync(targetUserId, ct);
        await _store.SaveChangesAsync(ct);

        // Audit bắt buộc cho hành động nhạy cảm (domain-rules.md §7).
        var action = locked ? "user.lock" : "user.unlock";
        await _audit.LogAsync(action, "User", targetUserId, adminUserId, reason, ct);

        // Trả summary mới.
        var updated = await _store.FindSummaryAsync(targetUserId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {targetUserId} not found after lock change.");
        return updated;
    }

    private static void ValidateQuery(AdminUserListQuery q)
    {
        if (q.Role is not null && q.Role is not ("member" or "pt" or "admin"))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "role phải là member|pt|admin",
                new Dictionary<string, string[]> { ["role"] = new[] { "member|pt|admin" } });

        if (q.Page < 1) q = q with { Page = 1 };
        if (q.PageSize is < 1) q = q with { PageSize = 25 };
        if (q.PageSize > 100) q = q with { PageSize = 100 };

        if (q.Sort is not ("createdAt:asc" or "createdAt:desc" or "email:asc" or "email:desc" or ""))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "sort không hợp lệ",
                new Dictionary<string, string[]> { ["sort"] = new[] { "allowlist only" } });
    }
}
