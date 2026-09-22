using Fitmate.Api.Auth;
using Fitmate.Api.Storage;
using Fitmate.Application.Storage;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Trainers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fitmate.Api.Trainers;

/// <summary>
/// BE-040 + BE-420: PT self-service certificate endpoints. The image upload is
/// done via the dedicated <c>/api/v1/uploads</c> endpoint and the resulting
/// <c>storageKey</c> is passed to <see cref="ICertificateService.SubmitAsync"/>.
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
    /// PT submit cert mới. <c>image_storage_key</c> comes from a prior
    /// <c>POST /api/v1/uploads</c> call (multipart) — see BE-420.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Certificate>> Submit(
        [FromServices] ICurrentUserAccessor current,
        [FromForm] string title,
        [FromForm] string issued_by,
        [FromForm] string code,
        [FromForm] DateOnly issue_date,
        [FromForm] string? image_storage_key,
        CancellationToken ct)
    {
        var userId = current.GetUserIdOrThrow();
        var cert = await _certs.SubmitAsync(userId, title, issued_by, code, issue_date, image_storage_key, ct);
        return CreatedAtAction(nameof(ListMine), new { }, cert);
    }
}
