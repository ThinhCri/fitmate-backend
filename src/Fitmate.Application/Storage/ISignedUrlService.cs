namespace Fitmate.Application.Storage;

/// <summary>
/// Abstraction for generating time-limited, HMAC-authenticated read URLs so
/// that binary blobs can be served by a CDN or static-file server without
/// a per-request proxy call through the API.
///
/// <para>
/// Clients receive a signed URL and can GET the resource directly.  The API
/// never proxies the bytes — it only checks the signature on the GET request
/// via <see cref="TryValidateAndExtractPath"/>.
/// </para>
///
/// <para>
/// Signed URL format:
/// <c>{BaseUrl}/{relativePath}?expires={unix_ts}&amp;sig={hmac_base64url}</c>
/// where the signature covers <c>"{relativePath}\n{unix_ts}"</c>.
/// </para>
public interface ISignedUrlService
{
    /// <summary>
    /// Generate a presigned read URL for the given relative path.  TTL is
    /// capped at <c>SignedUrlTtlMinutes</c> from <see cref="StorageOptions"/>.
    /// </summary>
    /// <param name="relativePath">Category-prefixed path, e.g. <c>avatars/user-123.jpg</c>.</param>
    /// <param name="ttl">Desired TTL. Will be clamped to the configured maximum.</param>
    /// <returns>Fully-formed URL with expiry and signature query parameters.</returns>
    Uri CreatePresignedReadUrl(string relativePath, TimeSpan ttl);

    /// <summary>
    /// Validate a signed URL's query parameters and, if valid, return the
    /// resolved relative path.  Used by the static-file middleware to gate
    /// access.
    /// </summary>
    /// <param name="pathAndQuery">The raw <c>Request.Path + Request.QueryString</c>.</param>
    /// <returns>
    /// The relative path on success; <c>null</c> if the signature is invalid
    /// or the URL has expired.
    /// </returns>
    string? TryValidateAndExtractPath(string pathAndQuery);
}
