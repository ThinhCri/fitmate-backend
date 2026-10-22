using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

/// <summary>
/// BE-040: Admin endpoints certificate (theo OpenAPI <c>/admin/certificates</c>).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/certificates")]
public class AdminCertificatesController : ControllerBase
{
    private readonly ICertificateService _certs;

    public AdminCertificatesController(ICertificateService certs)
    {
        _certs = certs;
    }

    /// <summary>
    /// Admin xem certificate của một PT, phân trang server-side. Bounded 1..100 theo <paramref name="pageSize"/>.
    /// </summary>
    [HttpGet("trainer/{ptUserId:guid}")]
    public async Task<IActionResult> ListTrainer(Guid ptUserId, [FromServices] ICurrentUserAccessor current,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var pageResult = await _certs.ListTrainerForAdminAsync(ptUserId, current.GetUserIdOrThrow(), page, pageSize, ct);
        return Ok(new AdminCertificatePage(pageResult.Items, pageResult.Total, pageResult.Page, pageResult.PageSize));
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, [FromServices] ICurrentUserAccessor current,
        [FromServices] Fitmate.Application.Storage.ISignedUrlService urls,
        [FromServices] IAuditLogger audit, CancellationToken ct)
    {
        var adminId = current.GetUserIdOrThrow();
        var cert = await _certs.GetForAdminAsync(id, adminId, ct);
        if (cert.ImageStorageKey is null) return NotFound();
        if (!cert.ImageStorageKey.StartsWith("certificates/", StringComparison.Ordinal))
            return NotFound();

        // CERT-02: prefix storage key KHÔNG chứng minh ownership.
        //   - NULL ImageUploadedByUserId = dữ liệu trước migration 20260928130000. Không suy đoán
        //     chủ sở hữu (không tự gán = PtUserId). Trả 404 để không tiết lộ sự tồn tại của
        //     tài nguyên mà ta không chứng minh được quyền.
        //   - Có chủ upload nhưng khác PT sở hữu cert → 403 (admin cố truy cập chứng chỉ
        //     được upload bởi tài khoản khác).
        if (!cert.ImageUploadedByUserId.HasValue) return NotFound();
        if (cert.ImageUploadedByUserId.Value != cert.PtUserId) return Forbid();

        var url = urls.CreatePresignedReadUrl(cert.ImageStorageKey, TimeSpan.FromMinutes(5));
        await audit.LogAsync("certificate.preview", "Certificate", id, adminId, null, ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new { url = url.ToString() });
    }

    [HttpGet("{id:guid}/contact")]
    public async Task<IActionResult> Contact(Guid id, [FromServices] ICurrentUserAccessor current, CancellationToken ct)
    {
        var result = await _certs.GetContactForAdminAsync(id, current.GetUserIdOrThrow(), ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, [FromServices] ICurrentUserAccessor current,
        [FromBody] RejectCertificateRequest body, CancellationToken ct) =>
        Ok(await _certs.RevokeAsync(id, current.GetUserIdOrThrow(), body.Reason, ct));

    /// <summary>List certificate Pending, phân trang.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminCertificatePage>> ListPending(
        [FromServices] ICurrentUserAccessor current,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var normalizedPage = page < 1 ? 1 : page;
        var normalizedPageSize = pageSize < 1 ? 50 : Math.Min(pageSize, 100);
        var (items, total) = await _certs.ListPendingAsync(current.GetUserIdOrThrow(), normalizedPage, normalizedPageSize, ct);
        return Ok(new AdminCertificatePage(items, total, normalizedPage, normalizedPageSize));
    }

    /// <summary>Approve certificate Pending.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<Certificate>> Approve(
        Guid id,
        [FromServices] ICurrentUserAccessor current,
        CancellationToken ct)
    {
        var adminId = current.GetUserIdOrThrow();
        var cert = await _certs.ApproveAsync(id, adminId, ct);
        return Ok(cert);
    }

    /// <summary>Reject certificate Pending (reason bắt buộc).</summary>
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<Certificate>> Reject(
        Guid id,
        [FromServices] ICurrentUserAccessor current,
        [FromBody] RejectCertificateRequest body,
        CancellationToken ct)
    {
        var adminId = current.GetUserIdOrThrow();
        var cert = await _certs.RejectAsync(id, adminId, body.Reason, ct);
        return Ok(cert);
    }

    [HttpPost("{id:guid}/reject-fraud")]
    public async Task<ActionResult<Certificate>> RejectFraud(
        Guid id,
        [FromServices] ICurrentUserAccessor current,
        [FromBody] RejectCertificateRequest body,
        CancellationToken ct) =>
        Ok(await _certs.RejectFraudAsync(id, current.GetUserIdOrThrow(), body.Reason, ct));
}

public sealed record RejectCertificateRequest(string Reason);

public sealed record AdminCertificatePage(
    IReadOnlyList<Certificate> Items,
    int Total,
    int Page,
    int PageSize);
