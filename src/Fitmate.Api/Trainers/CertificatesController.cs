using Fitmate.Api.Auth;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.Api.Trainers;

/// <summary>
/// BE-040: PT self-service certificate endpoints (theo OpenAPI <c>/me/certificates</c>).
/// </summary>
[ApiController]
[Authorize(Roles = "PT")]
[Route("api/v1/me/certificates")]
public class CertificatesController : ControllerBase
{
    private readonly ICertificateService _certs;

    public CertificatesController(ICertificateService certs)
    {
        _certs = certs;
    }

    /// <summary>List certificate của PT hiện tại.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Certificate>>> ListMine(
        [FromServices] ICurrentUserAccessor current,
        CancellationToken ct)
    {
        var userId = current.GetUserIdOrThrow();
        var items = await _certs.ListMineAsync(userId, ct);
        return Ok(items);
    }

    /// <summary>
    /// PT submit cert mới. Multipart upload theo OpenAPI.
    /// <c>imageStorageKey</c> từ upload service (chưa có — để null cho phase này; sẽ wire upload ở BE-420).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Certificate>> Submit(
        [FromServices] ICurrentUserAccessor current,
        [FromForm] string title,
        [FromForm] string issued_by,
        [FromForm] string code,
        [FromForm] DateOnly issue_date,
        CancellationToken ct)
    {
        var userId = current.GetUserIdOrThrow();
        var cert = await _certs.SubmitAsync(userId, title, issued_by, code, issue_date, imageStorageKey: null, ct);
        return CreatedAtAction(nameof(ListMine), new { }, cert);
    }
}
