using Fitmate.Application.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace Fitmate.Api.Storage;

/// <summary>
/// BE-420: File upload endpoint. Accepts multipart/form-data, stores the file
/// locally, and returns a time-limited HMAC-signed URL for direct access.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/uploads")]
public sealed class UploadsController : ControllerBase
{
    private readonly IStorageService _storage;
    private readonly ISignedUrlService _signedUrl;
    private readonly StorageOptions _options;
    private readonly ILogger<UploadsController> _logger;

    private static readonly Regex FilenameSanitizer = new(@"[^a-zA-Z0-9_\-.]", RegexOptions.Compiled);

    public UploadsController(
        IStorageService storage,
        ISignedUrlService signedUrl,
        IOptions<StorageOptions> options,
        ILogger<UploadsController> logger)
    {
        _storage = storage;
        _signedUrl = signedUrl;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Upload a file and receive a time-limited signed URL.</summary>
    [HttpPost]
    [RequestSizeLimit(5 * 1024 * 1024)] // 5 MiB
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadRequest request,
        CancellationToken ct)
    {
        // 1) Validate category.
        if (!_options.AllowedCategories.Contains(request.Category, StringComparer.OrdinalIgnoreCase))
            return BadRequest(MakeProblem(400, "CATEGORY_INVALID", "Category invalid",
                $"Category must be one of: {string.Join(", ", _options.AllowedCategories)}"));

        // 2) Validate file present.
        if (request.File is null || request.File.Length == 0)
            return BadRequest(MakeProblem(400, "FILE_REQUIRED", "File required", "No file was provided."));

        // 3) Validate size.
        if (request.File.Length > _options.MaxFileSizeBytes)
            return BadRequest(MakeProblem(400, "FILE_TOO_LARGE", "File too large",
                $"File exceeds maximum size of {_options.MaxFileSizeBytes / (1024 * 1024)} MiB."));

        // 4) Validate content type.
        var contentType = request.File.ContentType?.ToLowerInvariant() ?? "";
        if (!_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(MakeProblem(400, "CONTENT_TYPE_INVALID", "Content type invalid",
                $"Content type must be one of: {string.Join(", ", _options.AllowedContentTypes)}"));

        // 5) Build storage key.
        var ext = GetExtension(contentType);
        var safeName = SanitizeFilename(Path.GetFileNameWithoutExtension(request.File.FileName));
        var storageKey = $"{request.Category}/{safeName}_{Guid.NewGuid():N}{ext}";

        // 6) Stream to storage.
        await using var stream = request.File.OpenReadStream();
        var uri = await _storage.UploadAsync(storageKey, stream, contentType, ct);

        // 7) Generate signed URL.
        var signedUrl = _signedUrl.CreatePresignedReadUrl(
            storageKey,
            TimeSpan.FromMinutes(_options.SignedUrlTtlMinutes));

        _logger.LogInformation("Upload: {Key} stored at {Uri}, signed URL expiring in {Minutes} min",
            storageKey, uri, _options.SignedUrlTtlMinutes);

        return Created(uri, new UploadResponse(storageKey, uri.ToString(), signedUrl.ToString()));
    }

    private static string GetExtension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ""
    };

    private static string SanitizeFilename(string name)
    {
        var sanitized = FilenameSanitizer.Replace(name, "_");
        return string.IsNullOrWhiteSpace(sanitized) ? "file" : sanitized[..Math.Min(sanitized.Length, 48)];
    }

    private ProblemDetails MakeProblem(int status, string code, string title, string detail)
        => Fitmate.Api.Errors.FitmateProblemDetails.Create(status, title, code,
            code.ToLowerInvariant().Replace('_', '-'), detail);
}

// ─── DTOs ────────────────────────────────────────────────────────────────────

/// <param name="File">The file binary (multipart/form-data field name).</param>
/// <param name="Category">Destination folder: avatars | certificates | posts.</param>
public sealed record UploadRequest(IFormFile? File, string Category);

public sealed record UploadResponse(string StorageKey, string Uri, string SignedUrl);
