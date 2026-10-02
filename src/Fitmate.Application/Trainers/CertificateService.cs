using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// DTO phân trang cho admin / PT list certificates.
/// </summary>
/// <param name="Items">Bản ghi trong trang hiện tại.</param>
/// <param name="Total">Tổng số bản ghi (giúp phân trang).</param>
/// <param name="Page">Trang đang trả về (1-based).</param>
/// <param name="PageSize">Kích thước trang đã chuẩn hoá.</param>
public sealed record CertificatePage(IReadOnlyList<Certificate> Items, int Total, int Page, int PageSize);

/// <summary>
/// Default <see cref="ICertificateService"/> impl dùng <see cref="ICertificateStore"/> + <see cref="IAuditLogger"/>.
/// </summary>
public class CertificateService : ICertificateService
{
    private readonly ICertificateStore _store;
    private readonly IAuditLogger _audit;
    private readonly IAuthorizationGuard _authz;

    public CertificateService(ICertificateStore store, IAuditLogger audit, IAuthorizationGuard authz)
    {
        _store = store;
        _audit = audit;
        _authz = authz;
    }

    public async Task<Certificate> SubmitAsync(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey, Guid? imageUploadedByUserId, CancellationToken ct)
    {
        // BE-090: chỉ PT được submit certificate của mình.
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        ValidateFields(title, issuedBy, code);

        var existing = await _store.FindByIdentityAsync(ptUserId, title, issuedBy, code, ct);
        if (existing is not null) return existing;

        var cert = Certificate.Submit(ptUserId, title, issuedBy, code, issueDate, imageStorageKey, imageUploadedByUserId);
        var profile = await _store.FindPtProfileAsync(ptUserId, ct);
        profile?.MarkPendingReview();
        await _store.AddAsync(cert, ct);
        await _store.SaveChangesAsync(ct);
        return cert;
    }

    public async Task<CertificatePage> ListMineAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        // PT chỉ xem của mình; trang này đã là _store.ListByPtPagedAsync đã cap pageSize.
        return await ListPagedAsync(ptUserId, page, pageSize, ct);
    }

    public async Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingAsync(Guid adminUserId, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 50 : Math.Min(pageSize, 100);
        return await _store.ListPendingPagedAsync(page, pageSize, ct);
    }

    public async Task<Certificate> ApproveAsync(Guid certId, Guid adminUserId, CancellationToken ct)
    {
        // BE-090: enforce role Admin ở Application, không chỉ controller filter.
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        var cert = await _store.FindByIdAsync(certId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Certificate {certId} not found.");

        cert.Approve(adminUserId); // throws ConflictException nếu không ở Pending
        var profile = await _store.FindPtProfileAsync(cert.PtUserId, ct);
        profile?.Approve(adminUserId);
        // Stage audit within the same save; if SaveChanges fails the audit is rolled back together with
        // the state change so the audit cannot disappear while the change survived.
        _audit.Stage("certificate.approve", "Certificate", certId, adminUserId);
        await _store.SaveChangesAsync(ct);
        return cert;
    }

    public async Task<Certificate> RejectAsync(Guid certId, Guid adminUserId, string reason, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Trainer.CertificateMissingReason,
                "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });
        if (reason.Length > 500)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason tối đa 500 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "max 500 chars" } });

        var cert = await _store.FindByIdAsync(certId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Certificate {certId} not found.");

        cert.Reject(adminUserId, reason); // throws ConflictException nếu không ở Pending
        var profile = await _store.FindPtProfileAsync(cert.PtUserId, ct);
        profile?.RequestMoreInformation(adminUserId, reason.Trim());
        _audit.Stage("certificate.reject", "Certificate", certId, adminUserId, reason);
        await _store.SaveChangesAsync(ct);
        return cert;
    }

    public async Task<Certificate> RejectFraudAsync(Guid certId, Guid adminUserId, string reason, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Length > 500)
            throw new ValidationException(ErrorCodes.Trainer.CertificateMissingReason, "Reason must contain 5 to 500 characters.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "5 to 500 characters" } });

        var cert = await _store.FindByIdAsync(certId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Certificate not found.");
        cert.Reject(adminUserId, reason.Trim());
        var profile = await _store.FindPtProfileAsync(cert.PtUserId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, "PT profile not found.");
        var user = await _store.FindUserAsync(cert.PtUserId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, "PT user not found.");
        profile.RejectFraud(adminUserId, reason.Trim());
        user.Status = UserStatus.Locked;
        user.UpdatedAt = DateTime.UtcNow;
        _audit.Stage("pt_application.reject_fraud", "PtProfile", profile.Id, adminUserId, reason.Trim());
        await _store.SaveChangesAsync(ct);
        return cert;
    }

    private static void ValidateFields(string title, string issuedBy, string code)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "title required, max 200 chars",
                new Dictionary<string, string[]> { ["title"] = new[] { "required, max 200 chars" } });
        if (string.IsNullOrWhiteSpace(issuedBy) || issuedBy.Length > 200)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "issuedBy required, max 200 chars",
                new Dictionary<string, string[]> { ["issuedBy"] = new[] { "required, max 200 chars" } });
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "code required, max 100 chars",
                new Dictionary<string, string[]> { ["code"] = new[] { "required, max 100 chars" } });
    }

    public async Task<Certificate> GetForAdminAsync(Guid id, Guid adminId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);
        return await _store.FindByIdAsync(id, ct) ?? throw new NotFoundException(ErrorCodes.General.NotFound, "Certificate not found.");
    }

    /// <summary>
    /// Admin xem certificate của một PT, có phân trang server-side.
    /// </summary>
    public async Task<CertificatePage> ListTrainerForAdminAsync(Guid ptUserId, Guid adminId, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminId, "Admin", ct);
        return await ListPagedAsync(ptUserId, page, pageSize, ct);
    }

    private async Task<CertificatePage> ListPagedAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct)
    {
        // Cap pageSize ở Application: chặn store bị gọi với tham số lớn bất thường.
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;
        var (items, total) = await _store.ListByPtPagedAsync(ptUserId, page, pageSize, ct);
        return new CertificatePage(items, total, page, pageSize);
    }

    public async Task<Certificate> RevokeAsync(Guid id, Guid adminId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationException(
                ErrorCodes.Trainer.CertificateMissingReason,
                "Reason phải có ít nhất 5 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "min 5 chars" } });
        if (reason.Length > 500)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reason tối đa 500 ký tự.",
                new Dictionary<string, string[]> { ["reason"] = new[] { "max 500 chars" } });

        var cert = await GetForAdminAsync(id, adminId, ct);
        cert.Revoke(adminId, reason);
        // Same-unit-of-work for audit + state change: revoke cannot happen without an audit row.
        _audit.Stage("certificate.revoke", "Certificate", id, adminId, reason);
        await _store.SaveChangesAsync(ct);
        return cert;
    }
}
