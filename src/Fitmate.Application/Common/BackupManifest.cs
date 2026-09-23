using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fitmate.Application.Common;

/// <summary>
/// BE-825: Manifest quản lý toàn vẹn backup cho Disaster Recovery.
/// Ràng buộc 3 thành phần cốt lõi:
/// 1. Database dump (PostgreSQL custom format pg_dump -Fc)
/// 2. DataProtection Key Ring (chứa cryptographic keys giải mã push tokens, verification tokens)
/// 3. Media Assets / Uploads (tài liệu, chứng chỉ PT, ảnh đại diện)
/// Toàn bộ thành phần đều được bảo vệ bằng mã băm SHA-256 để chống can thiệp hoặc lỗi suy hao dữ liệu.
/// </summary>
public record BackupComponent
{
    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }
}

public class BackupManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [JsonPropertyName("manifest_version")]
    public string ManifestVersion { get; init; } = "1.0";

    [JsonPropertyName("backup_id")]
    public string BackupId { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("environment")]
    public string Environment { get; init; } = "Production";

    [JsonPropertyName("app_version")]
    public string AppVersion { get; init; } = "1.0.0";

    [JsonPropertyName("engine_version")]
    public string EngineVersion { get; init; } = "PostgreSQL 16";

    /// <summary>
    /// Mốc snapshot nhất quán của cơ sở dữ liệu (thời điểm pg_dump bắt đầu export snapshot transaction).
    /// Đây là mốc chân lý (Authoritative Baseline) để đồng bộ với Media và Key Ring.
    /// </summary>
    [JsonPropertyName("snapshot_timestamp_utc")]
    public DateTimeOffset SnapshotTimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Phiên bản EF Core migration / schema version tương ứng với DB snapshot.
    /// </summary>
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "20260918144613_InitialCreate";

    /// <summary>
    /// Log Sequence Number (LSN) hoặc Transaction Id của PostgreSQL tại mốc snapshot (nếu có).
    /// </summary>
    [JsonPropertyName("db_snapshot_lsn")]
    public string? DbSnapshotLsn { get; init; }

    /// <summary>
    /// Mốc bắt đầu khoảng thời gian cần đối soát khi khôi phục (bằng SnapshotTimestampUtc).
    /// </summary>
    [JsonPropertyName("reconciliation_window_start_utc")]
    public DateTimeOffset ReconciliationWindowStartUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Canary Token ciphertext được mã hóa bằng DataProtection Key Ring trước khi backup
    /// để diễn tập giải mã thực tế sau khi restore.
    /// </summary>
    [JsonPropertyName("canary_ciphertext")]
    public string? CanaryCiphertext { get; init; }

    [JsonPropertyName("canary_purpose")]
    public string? CanaryPurpose { get; init; }

    [JsonPropertyName("database")]
    public BackupComponent Database { get; init; } = new();

    [JsonPropertyName("key_ring")]
    public BackupComponent KeyRing { get; init; } = new();

    [JsonPropertyName("media_assets")]
    public BackupComponent? MediaAssets { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>
    /// Tính chuỗi băm SHA-256 dạng hex chữ thường từ mảng bytes.
    /// </summary>
    public static string ComputeSha256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Tính chuỗi băm SHA-256 dạng hex chữ thường từ luồng file stream.
    /// </summary>
    public static string ComputeFileSha256(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found for SHA256 calculation: {filePath}", filePath);
        }

        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Serialize đối tượng manifest sang JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>
    /// Parse JSON thành đối tượng BackupManifest.
    /// </summary>
    public static BackupManifest FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize backup manifest JSON.");
    }

    /// <summary>
    /// Kiểm tra tính toàn vẹn của tất cả các file được chỉ định trong manifest tại thư mục backup.
    /// Trả về true nếu tất cả file tồn tại và hash SHA-256 khớp 100%.
    /// </summary>
    public (bool IsValid, IReadOnlyList<string> Errors) ValidateIntegrity(string backupDirectory)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(backupDirectory) || !Directory.Exists(backupDirectory))
        {
            errors.Add($"Backup directory does not exist: {backupDirectory}");
            return (false, errors);
        }

        // 1. Kiểm tra Database dump
        ValidateComponent(backupDirectory, Database, "Database", errors, required: true);

        // 2. Kiểm tra DataProtection key ring
        ValidateComponent(backupDirectory, KeyRing, "KeyRing", errors, required: true);

        // 3. Kiểm tra Media assets (nếu có định nghĩa)
        if (MediaAssets != null && !string.IsNullOrWhiteSpace(MediaAssets.FileName))
        {
            ValidateComponent(backupDirectory, MediaAssets, "MediaAssets", errors, required: false);
        }

        return (errors.Count == 0, errors);
    }

    private static void ValidateComponent(
        string baseDir,
        BackupComponent component,
        string componentName,
        List<string> errors,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(component.FileName))
        {
            if (required)
            {
                errors.Add($"Required component '{componentName}' has no file_name defined.");
            }
            return;
        }

        var fullPath = Path.Combine(baseDir, component.FileName);
        if (!File.Exists(fullPath))
        {
            errors.Add($"Component '{componentName}' file not found: {component.FileName}");
            return;
        }

        var actualSize = new FileInfo(fullPath).Length;
        if (component.SizeBytes > 0 && actualSize != component.SizeBytes)
        {
            errors.Add($"Component '{componentName}' size mismatch: expected {component.SizeBytes} bytes, actual {actualSize} bytes.");
        }

        var actualHash = ComputeFileSha256(fullPath);
        if (!string.Equals(actualHash, component.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Component '{componentName}' SHA-256 checksum mismatch: expected {component.Sha256}, actual {actualHash}.");
        }
    }
}
