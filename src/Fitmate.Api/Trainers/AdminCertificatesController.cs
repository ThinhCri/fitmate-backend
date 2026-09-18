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

    /// <summary>List certificate Pending, phân trang.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminCertificatePage>> ListPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var (items, total) = await _certs.ListPendingAsync(page, pageSize, ct);
        return Ok(new AdminCertificatePage(items, total, page, pageSize));
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
}

public sealed record RejectCertificateRequest(string Reason);

public sealed record AdminCertificatePage(
    IReadOnlyList<Certificate> Items,
    int Total,
    int Page,
    int PageSize);
