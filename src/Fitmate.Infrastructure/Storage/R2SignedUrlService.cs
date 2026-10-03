using Amazon.S3;
using Amazon.S3.Model;
using Fitmate.Application.Storage;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Storage;

public sealed class R2SignedUrlService : ISignedUrlService
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly int _maxTtlMinutes;

    public R2SignedUrlService(IAmazonS3 client, IOptions<StorageOptions> options)
    {
        _client = client;
        _bucket = options.Value.R2BucketName
            ?? throw new InvalidOperationException("Storage:R2BucketName must be configured.");
        _maxTtlMinutes = options.Value.SignedUrlTtlMinutes;
    }

    public Uri CreatePresignedReadUrl(string relativePath, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var clamped = ttl > TimeSpan.FromMinutes(_maxTtlMinutes)
            ? TimeSpan.FromMinutes(_maxTtlMinutes)
            : ttl;
        var url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = relativePath.Replace('\\', '/').TrimStart('/'),
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(clamped)
        });
        return new Uri(url);
    }

    public string? TryValidateAndExtractPath(string pathAndQuery) => null;
}
