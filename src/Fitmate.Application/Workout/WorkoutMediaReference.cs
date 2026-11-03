using System.Text.RegularExpressions;
using Fitmate.Application.Storage;

namespace Fitmate.Application.Workout;

/// <summary>Only shared exercise-catalog images may be re-signed without private-media ownership checks.</summary>
public static class WorkoutMediaReference
{
    private static readonly Regex CatalogKey = new(@"^exercise-catalog/[A-Za-z0-9_-]+/[01]\.jpg$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string? NormalizeForStorage(string? value)
    {
        var key = GetCatalogKey(value);
        return key is null ? value : "storage://" + key;
    }

    public static string? ForRead(string? value, ISignedUrlService? signedUrls)
    {
        var key = GetCatalogKey(value);
        return key is null || signedUrls is null ? value : signedUrls.CreatePresignedReadUrl(key, TimeSpan.FromMinutes(15)).ToString();
    }

    private static string? GetCatalogKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8000) return null;
        string path;
        if (value.StartsWith("storage://", StringComparison.OrdinalIgnoreCase)) path = value[10..];
        else if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)) path = value.Split('?')[0];
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) path = uri.AbsolutePath;
        else return null;
        // R2 uses /bucket/key; local signed URLs use /uploads/key. Ignore expired
        // signatures only inside the shared catalog namespace, never posts/certs/chat.
        const string prefix = "exercise-catalog/";
        var index = path.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0 || (index > 0 && path[index - 1] != '/')) return null;
        var candidate = path[index..];
        return CatalogKey.IsMatch(candidate) ? candidate : null;
    }
}
