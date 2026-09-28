using System.Text.Json.Serialization;
using Fitmate.Api.Auth;
using Fitmate.Api.Storage;
using Fitmate.Application.Storage;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
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

    /// <summary>List certificate của PT hiện tại (phân trang).</summary>
    [HttpGet]
    public async Task<ActionResult<CertificatePageResponse>> ListMine(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] ISignedUrlService urls,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var userId = current.GetUserIdOrThrow();
        var pageResult = await _certs.ListMineAsync(userId, page, pageSize, ct);
        var responseItems = pageResult.Items.Select(c => ToResponse(c, urls)).ToList();
        return Ok(new CertificatePageResponse(responseItems, pageResult.Total, pageResult.Page, pageResult.PageSize));
    }

    /// <summary>
    /// PT submit cert mới. <c>image_storage_key</c> comes from a prior
    /// <c>POST /api/v1/uploads</c> call (multipart) — see BE-420.
    ///
    /// CERT-02: <c>UploadsController</c> scopes the storage key as
    /// <c>{category}/{userIdN}_{file}_{random}.{ext}</c>. We re-check that scope here so a
    /// PT cannot claim another account's key. Recording
    /// <see cref="Certificate.ImageUploadedByUserId"/> alone would not be enough, because
    /// the client supplies the key and could supply someone else's.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CertificateResponse>> Submit(
        [FromServices] ICurrentUserAccessor current,
        [FromServices] ISignedUrlService urls,
        [FromForm] string title,
        [FromForm] string issued_by,
        [FromForm] string code,
        [FromForm] DateOnly issue_date,
        [FromForm] string? image_storage_key,
        CancellationToken ct)
    {
        var userId = current.GetUserIdOrThrow();
        if (string.IsNullOrWhiteSpace(image_storage_key))
        {
            var noImage = await _certs.SubmitAsync(userId, title, issued_by, code, issue_date, null, null, ct);
            return CreatedAtAction(nameof(ListMine), new { }, ToResponse(noImage, urls));
        }

        if (!ImageKeyBelongsTo(image_storage_key, userId))
            throw new DomainException("CERT_IMAGE_NOT_OWNED", "The certificate image was not uploaded by this account.", 403);

        var cert = await _certs.SubmitAsync(userId, title, issued_by, code, issue_date, image_storage_key, userId, ct);
        return CreatedAtAction(nameof(ListMine), new { }, ToResponse(cert, urls));
    }

    /// <summary>
    /// A key issued by <c>UploadsController</c> starts with <c>{category}/{userIdN}_</c>.
    /// The caller is already restricted to the PT role, so any category is acceptable here;
    /// what matters is that the embedded scope matches the submitting account.
    /// </summary>
    internal static bool ImageKeyBelongsTo(string storageKey, Guid userId)
    {
        var trimmed = storageKey.Trim();
        var separator = trimmed.IndexOf('/');
        if (separator <= 0 || separator == trimmed.Length - 1) return false;
        var fileName = trimmed[(separator + 1)..];
        var expectedPrefix = userId.ToString("N") + "_";
        return fileName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static CertificateResponse ToResponse(Certificate c, ISignedUrlService? urls)
    {
        string? imageUrl = null;
        if (!string.IsNullOrWhiteSpace(c.ImageStorageKey))
        {
            if (urls != null)
            {
                try
                {
                    imageUrl = urls.CreatePresignedReadUrl(c.ImageStorageKey, TimeSpan.FromMinutes(60)).ToString();
                }
                catch
                {
                    imageUrl = c.ImageStorageKey;
                }
            }
            else
            {
                imageUrl = c.ImageStorageKey;
            }
        }

        return new CertificateResponse(
            c.Id,
            c.PtUserId,
            c.Title,
            c.IssuedBy,
            c.Code,
            c.IssueDate.ToString("yyyy-MM-dd"),
            c.Status.ToString().ToLowerInvariant(),
            imageUrl,
            c.RejectReason,
            c.VerifiedAt);
    }
}

public sealed record CertificateResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("pt_id")] Guid PtId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("issued_by")] string IssuedBy,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("issue_date")] string IssueDate,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("image_url")] string? ImageUrl,
    [property: JsonPropertyName("reject_reason")] string? RejectReason,
    [property: JsonPropertyName("verified_at")] DateTime? VerifiedAt);

public sealed record CertificatePageResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<CertificateResponse> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize);
