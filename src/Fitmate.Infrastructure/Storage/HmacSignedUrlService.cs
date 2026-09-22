using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Storage;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Storage;

/// <summary>
/// HMAC-SHA256 presigned URL generator and validator.
///
/// <para>
/// Signed URL format:
/// <c>{BaseUrl}/{relativePath}?expires={unix_ts}&amp;sig={hmac_base64url}</c>
///
/// Signature covers: <c>"{relativePath}\n{unix_ts}"</c> using HMAC-SHA256.
/// </para>
///
/// <para>
/// <c>CreatePresignedReadUrl</c> generates a URL the client uses to GET the file
/// directly from the static-file endpoint. <c>TryValidateAndExtractPath</c> is
/// called by the static-file middleware to gate access.
/// </para>
public sealed class HmacSignedUrlService : ISignedUrlService
{
    private readonly string _baseUrl;
    private readonly byte[] _secret;
    private readonly int _maxTtlMinutes;

    public HmacSignedUrlService(IOptions<StorageOptions> options)
    {
        var opt = options.Value;
        _baseUrl = opt.BaseUrl.TrimEnd('/');
        _secret = Encoding.UTF8.GetBytes(opt.SignedUrlSecret);
        _maxTtlMinutes = opt.SignedUrlTtlMinutes;
    }

    /// <inheritdoc />
    public Uri CreatePresignedReadUrl(string relativePath, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var clamped = ttl > TimeSpan.FromMinutes(_maxTtlMinutes)
            ? TimeSpan.FromMinutes(_maxTtlMinutes)
            : ttl;

        var expiresUnix = DateTimeOffset.UtcNow.Add(clamped).ToUnixTimeSeconds();
        var signature = ComputeSignature(relativePath, expiresUnix);

        var sb = new StringBuilder(_baseUrl);
        sb.Append('/');
        sb.Append(UrlEncodePath(relativePath));
        sb.AppendFormat("?expires={0}&sig={1}", expiresUnix, signature);

        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    /// <inheritdoc />
    public string? TryValidateAndExtractPath(string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery))
            return null;

        var queryIndex = pathAndQuery.IndexOf('?');
        if (queryIndex < 0)
            return null;

        var rawPath = pathAndQuery[..queryIndex];
        var query = pathAndQuery[(queryIndex + 1)..];

        // Parse query string manually to avoid extra allocations.
        long? expiresUnix = null;
        string? sig = null;
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var key = pair[..eq];
            var val = pair[(eq + 1)..];
            if (key == "expires")
                expiresUnix = long.TryParse(val, out var e) ? e : null;
            else if (key == "sig")
                sig = val;
        }

        if (!expiresUnix.HasValue || string.IsNullOrEmpty(sig))
            return null;

        // Reject expired URLs.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expiresUnix.Value < nowUnix)
            return null;

        // Extract the path (URL-decode it) and validate the signature.
        var rawRelative = UrlDecodePath(rawPath);
        // Strip the leading /uploads/ (or whatever the storage prefix is) so the
        // returned path is relative to the storage root.
        var uploadsIdx = rawRelative.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
        var relativePath = uploadsIdx >= 0
            ? rawRelative[(uploadsIdx + "/uploads/".Length)..]
            : rawRelative.TrimStart('/');
        var expectedSig = ComputeSignature(relativePath, expiresUnix.Value);

        // Constant-time comparison to prevent timing attacks.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(sig)))
            return null;

        return relativePath;
    }

    /// <summary>HMAC-SHA256 over "{relativePath}\n{unix_ts}" → base64url (no padding).</summary>
    private string ComputeSignature(string relativePath, long expiresUnix)
    {
        var message = $"{relativePath}\n{expiresUnix}";
        var hmac = HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(message));
        return Convert.ToBase64String(hmac)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // Minimal URL-encode for path segments (no leading slash, no double-encoding).
    private static string UrlEncodePath(string path)
    {
        var sb = new StringBuilder(path.Length * 2);
        foreach (var c in path)
        {
            if (IsUnreserved(c))
                sb.Append(c);
            else
                sb.Append('%').Append(((int)c).ToString("X2"));
        }
        return sb.ToString();
    }

    private static string UrlDecodePath(string path)
    {
        var sb = new StringBuilder(path.Length);
        var i = 0;
        while (i < path.Length)
        {
            if (path[i] == '%' && i + 2 < path.Length)
            {
                sb.Append((char)Convert.ToByte(path[(i + 1)..(i + 3)], 16));
                i += 3;
            }
            else if (path[i] == '+')
            {
                sb.Append(' ');
                i++;
            }
            else
            {
                sb.Append(path[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    // RFC 3986 unreserved + path-separator. Forward slash is allowed in path
    // segments; only encode when needed for actual safety.
    private static bool IsUnreserved(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
        (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~' || c == '/';
}
