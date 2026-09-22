namespace Fitmate.Application.Storage;

/// <summary>
/// Abstraction for persisting binary blobs (files, images, documents).
///
/// <para>
/// Implementations may target local disk (dev), cloud object storage (S3, GCS),
/// or a CDN. Callers must not make assumptions about the backing store.
/// </para>
///
/// <para>
/// Paths are relative to the storage root. Implementations normalise separators
/// but MUST NOT allow path traversal. Allowed categories are controlled by
/// <see cref="StorageOptions.AllowedCategories"/> and are enforced by the
/// Application layer before calling this service.
/// </para>
public interface IStorageService
{
    /// <summary>
    /// Persist a stream to the given relative path. The file is created
    /// atomically (write-to-temp-then-rename) so partial writes are never visible.
    /// </summary>
    /// <param name="relativePath">Category-prefixed path, e.g. <c>avatars/user-123.jpg</c>.</param>
    /// <param name="data">Readable stream. Caller is responsible for disposal.</param>
    /// <param name="contentType">MIME type, e.g. <c>image/jpeg</c>.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Absolute URI of the stored file.</returns>
    Task<Uri> UploadAsync(
        string relativePath,
        Stream data,
        string contentType,
        CancellationToken ct = default);
}
