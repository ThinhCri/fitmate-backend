namespace Fitmate.Application.Storage;

/// <summary>
/// Configuration options for the storage subsystem.
/// Bind from <c>Storage</c> config section.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Storage provider: <c>local</c> for development or <c>r2</c> for Cloudflare R2.</summary>
    public string Backend { get; set; } = "local";

    /// <summary>Absolute or relative path where files are stored on disk.</summary>
    public string BasePath { get; set; } = "./uploads";

    /// <summary>Public-facing base URL for generated URLs.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5116/uploads";

    /// <summary>Maximum file size in bytes. Default 5 MiB.</summary>
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>MIME types allowed for upload.</summary>
    public IReadOnlyList<string> AllowedContentTypes { get; set; } = new[]
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    /// <summary>Allowed top-level categories (folder names under BasePath).</summary>
    public IReadOnlyList<string> AllowedCategories { get; set; } = new[]
    {
        "avatars",
        "certificates",
        "posts"
    };

    /// <summary>Secret key for HMAC-SHA256 signed URL generation (min 32 chars).</summary>
    public string SignedUrlSecret { get; set; } = "dev-secret-min-32-chars-change-in-prod!!";

    /// <summary>Default TTL for presigned URLs in minutes. Max 15.</summary>
    public int SignedUrlTtlMinutes { get; set; } = 15;

    public string? R2AccountId { get; set; }
    public string? R2AccessKeyId { get; set; }
    public string? R2SecretAccessKey { get; set; }
    public string? R2BucketName { get; set; }
}
