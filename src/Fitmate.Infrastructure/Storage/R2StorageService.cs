using Amazon.S3;
using Amazon.S3.Model;
using Fitmate.Application.Storage;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Storage;

public sealed class R2StorageService : IStorageService, IOwnedStorageKeyListing
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly IReadOnlyList<string> _categories;

    public R2StorageService(IAmazonS3 client, IOptions<StorageOptions> options)
    {
        _client = client;
        _bucket = options.Value.R2BucketName
            ?? throw new InvalidOperationException("Storage:R2BucketName must be configured.");
        _categories = options.Value.AllowedCategories;
    }

    public async Task<Uri> UploadAsync(string relativePath, Stream data, string contentType, CancellationToken ct = default)
    {
        var key = NormalizeKey(relativePath);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = data,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        }, ct);

        return new Uri($"r2://{_bucket}/{Uri.EscapeDataString(key).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}");
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        => _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = NormalizeKey(relativePath) }, ct);

    public async Task<IReadOnlyList<string>> ListOwnedKeysAsync(Guid userId, CancellationToken ct = default)
    {
        var result = new List<string>();
        foreach (var category in _categories)
        {
            string? continuation = null;
            do
            {
                var response = await _client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = category.TrimEnd('/') + "/" + userId.ToString("N") + "_",
                    ContinuationToken = continuation
                }, ct);
                result.AddRange(response.S3Objects.Select(x => x.Key));
                continuation = response.IsTruncated == true ? response.NextContinuationToken : null;
            } while (continuation is not null);
        }
        return result;
    }

    private static string NormalizeKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var key = value.Replace('\\', '/').TrimStart('/');
        if (key.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidOperationException("Invalid object storage key.");
        return key;
    }
}
