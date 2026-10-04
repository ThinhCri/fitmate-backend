using Fitmate.Application.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Storage;

/// <summary>
/// Local-disk <see cref="IStorageService"/> implementation for development.
///
/// <para>
/// Files are written to <c>{BasePath}/{relativePath}</c>.  The directory
/// tree is created on demand.  Writes use a temporary staging file in the
/// same directory and are atomically renamed to the final name so that readers
/// never see a partial file.
/// </para>
///
/// <para>
/// Security note: callers are responsible for validating <paramref name="relativePath"/>
/// against <see cref="StorageOptions.AllowedCategories"/> before calling this service.
/// </para>
public sealed class LocalStorageService : IStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalStorageService> _logger;

    public LocalStorageService(IOptions<StorageOptions> options, ILogger<LocalStorageService> logger)
    {
        _basePath = Path.GetFullPath(options.Value.BasePath);
        _logger = logger;
    }

    public async Task<Uri> UploadAsync(
        string relativePath,
        Stream data,
        string contentType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(contentType);

        var fullPath = GetFullPath(relativePath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException();

        Directory.CreateDirectory(directory);

        var tmpPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        await using (var tmpStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await data.CopyToAsync(tmpStream, ct);
            await tmpStream.FlushAsync(ct);
        }

        try
        {
            // Atomic rename: visible only when write is complete.
            File.Move(tmpPath, fullPath, overwrite: false);
        }
        catch (IOException) when (File.Exists(fullPath))
        {
            // Race: another upload wrote the same path first. Accept (idempotent).
            File.Delete(tmpPath);
            _logger.LogDebug("Storage: race-wrote {Path}, accepted first writer", relativePath);
        }

        _logger.LogDebug("Storage: uploaded {Path} ({ContentType}, {Size} bytes)",
            relativePath, contentType, new FileInfo(fullPath).Length);

        // Return the absolute file:// URI so callers can construct public URLs from it.
        return new Uri(fullPath);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string GetFullPath(string relativePath)
    {
        // Normalise to forward-slash so the split is reliable on Windows.
        var normalised = relativePath.Replace('\\', '/');
        var combined = Path.Combine(_basePath, normalised);
        var full = Path.GetFullPath(combined);

        // Fail loudly if the resolved path escapes the base directory (path traversal).
        if (!full.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path traversal attempt detected: {relativePath}");

        return full;
    }
}
