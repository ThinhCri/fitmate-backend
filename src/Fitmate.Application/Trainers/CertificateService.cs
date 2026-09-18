using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

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

    public async Task<Certificate> SubmitAsync(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey, CancellationToken ct)
    {
        // BE-090: chỉ PT được submit certificate của mình.
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        ValidateFields(title, issuedBy, code);

        var existing = await _store.FindByIdentityAsync(ptUserId, title, issuedBy, code, ct);
        if (existing is not null) return existing;

        var cert = Certificate.Submit(ptUserId, title, issuedBy, code, issueDate, imageStorageKey);
        await _store.AddAsync(cert, ct);
        await _store.SaveChangesAsync(ct);
        return cert;
    }

    public async Task<IReadOnlyList<Certificate>> ListMineAsync(Guid ptUserId, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(ptUserId, "PT", ct);
        return await _store.ListByPtAsync(ptUserId, ct);
    }

    public Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingAsync(int page, int pageSize, CancellationToken ct)
        => _store.ListPendingPagedAsync(page, pageSize, ct);

    public async Task<Certificate> ApproveAsync(Guid certId, Guid adminUserId, CancellationToken ct)
    {
        // BE-090: enforce role Admin ở Application, không chỉ controller filter.
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        var cert = await _store.FindByIdAsync(certId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"Certificate {certId} not found.");

        cert.Approve(adminUserId); // throws ConflictException nếu không ở Pending
        await _store.SaveChangesAsync(ct);
        await _audit.LogAsync("certificate.approve", "Certificate", certId, adminUserId, reason: null, ct);
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
        await _store.SaveChangesAsync(ct);
        await _audit.LogAsync("certificate.reject", "Certificate", certId, adminUserId, reason, ct);
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
}
