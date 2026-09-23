using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Common;
using Fitmate.Infrastructure.Common;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-825: Unit tests cho Backup Manifest, tính toán và phát hiện giả mạo SHA-256,
/// DataProtection Canary Token roundtrip và các bất biến phục hồi thảm họa.
/// </summary>
public class BackupAndRetentionTests
{
    [Fact]
    public void Manifest_Serialization_And_Deserialization_Preserves_Snapshot_Coordinates()
    {
        var snapshotUtc = DateTimeOffset.UtcNow;
        var manifest = new BackupManifest
        {
            BackupId = "backup_20260923_120000",
            SnapshotTimestampUtc = snapshotUtc,
            SchemaVersion = "20260918144613_InitialCreate",
            DbSnapshotLsn = "0/16B23C8",
            ReconciliationWindowStartUtc = snapshotUtc,
            CanaryCiphertext = "cipher-token-xyz",
            CanaryPurpose = PostRestoreInvariantChecker.DefaultCanaryPurpose,
            Database = new BackupComponent
            {
                FileName = "fitmate_db.dump",
                Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                SizeBytes = 1048576,
                ContentType = "application/x-pg-dump-custom",
                ItemCount = 1
            },
            KeyRing = new BackupComponent
            {
                FileName = "dpkeys.tar.gz",
                Sha256 = "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb",
                SizeBytes = 4096,
                ContentType = "application/gzip",
                ItemCount = 2
            }
        };

        var json = manifest.ToJson();
        json.Should().NotBeNullOrWhiteSpace();

        var deserialized = BackupManifest.FromJson(json);
        deserialized.Should().NotBeNull();
        deserialized.BackupId.Should().Be("backup_20260923_120000");
        deserialized.SchemaVersion.Should().Be("20260918144613_InitialCreate");
        deserialized.DbSnapshotLsn.Should().Be("0/16B23C8");
        deserialized.Database.FileName.Should().Be("fitmate_db.dump");
        deserialized.Database.SizeBytes.Should().Be(1048576);
        deserialized.KeyRing.FileName.Should().Be("dpkeys.tar.gz");
    }

    [Fact]
    public void Manifest_ComputeSha256_Calculates_Deterministic_Hash()
    {
        var rawData = Encoding.UTF8.GetBytes("Fitmate Disaster Recovery Test Content");
        var hash1 = BackupManifest.ComputeSha256(rawData);
        var hash2 = BackupManifest.ComputeSha256(rawData);

        hash1.Should().Be(hash2);
        hash1.Length.Should().Be(64); // 256 bits = 64 hex characters
    }

    [Fact]
    public void Manifest_ValidateIntegrity_Detects_Corrupted_Or_Tampered_Files()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fitmate_test_backup_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbFile = Path.Combine(tempDir, "fitmate_db.dump");
            var keyFile = Path.Combine(tempDir, "dpkeys.tar.gz");

            var originalDbBytes = Encoding.UTF8.GetBytes("Postgres custom binary dump content 12345");
            var originalKeyBytes = Encoding.UTF8.GetBytes("DataProtection XML key ring bytes 67890");

            File.WriteAllBytes(dbFile, originalDbBytes);
            File.WriteAllBytes(keyFile, originalKeyBytes);

            var dbHash = BackupManifest.ComputeFileSha256(dbFile);
            var keyHash = BackupManifest.ComputeFileSha256(keyFile);

            var manifest = new BackupManifest
            {
                BackupId = "test_tamper",
                Database = new BackupComponent
                {
                    FileName = "fitmate_db.dump",
                    Sha256 = dbHash,
                    SizeBytes = originalDbBytes.Length
                },
                KeyRing = new BackupComponent
                {
                    FileName = "dpkeys.tar.gz",
                    Sha256 = keyHash,
                    SizeBytes = originalKeyBytes.Length
                }
            };

            // 1. Khi chưa bị sửa: ValidateIntegrity hợp lệ
            var (isValidOriginal, originalErrors) = manifest.ValidateIntegrity(tempDir);
            isValidOriginal.Should().BeTrue();
            originalErrors.Should().BeEmpty();

            // 2. Can thiệp sửa đổi 1 byte trong file DB dump
            var corruptedDbBytes = (byte[])originalDbBytes.Clone();
            corruptedDbBytes[0] ^= 0xFF; // Flip bits
            File.WriteAllBytes(dbFile, corruptedDbBytes);

            // ValidateIntegrity phải phát hiện sai khác SHA-256 và fail-fast
            var (isValidCorrupted, corruptedErrors) = manifest.ValidateIntegrity(tempDir);
            isValidCorrupted.Should().BeFalse();
            corruptedErrors.Should().Contain(e => e.Contains("SHA-256 checksum mismatch"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Manifest_ValidateIntegrity_Detects_Missing_Required_Component()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fitmate_missing_comp_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var keyFile = Path.Combine(tempDir, "dpkeys.tar.gz");
            File.WriteAllBytes(keyFile, new byte[] { 1, 2, 3 });

            var manifest = new BackupManifest
            {
                BackupId = "test_missing",
                Database = new BackupComponent
                {
                    FileName = "missing_db.dump",
                    Sha256 = "abc",
                    SizeBytes = 100
                },
                KeyRing = new BackupComponent
                {
                    FileName = "dpkeys.tar.gz",
                    Sha256 = BackupManifest.ComputeFileSha256(keyFile),
                    SizeBytes = 3
                }
            };

            var (isValid, errors) = manifest.ValidateIntegrity(tempDir);
            isValid.Should().BeFalse();
            errors.Should().Contain(e => e.Contains("file not found: missing_db.dump"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void DataProtection_CanaryToken_Decrypts_With_Matching_KeyRing_And_Fails_With_Different_KeyRing()
    {
        var tempKeysDirA = Path.Combine(Path.GetTempPath(), "fitmate_keys_a_" + Guid.NewGuid());
        var tempKeysDirB = Path.Combine(Path.GetTempPath(), "fitmate_keys_b_" + Guid.NewGuid());
        Directory.CreateDirectory(tempKeysDirA);
        Directory.CreateDirectory(tempKeysDirB);

        try
        {
            // 1. Tạo provider A trỏ vào thư mục keys A
            var servicesA = new ServiceCollection();
            servicesA.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(tempKeysDirA))
                .SetApplicationName("Fitmate.Api");
            using var spA = servicesA.BuildServiceProvider();
            var providerA = spA.GetRequiredService<IDataProtectionProvider>();

            // Tạo Canary Ciphertext
            var expectedPlaintext = "fitmate-dr-canary-token-alive";
            var ciphertext = DataProtectionCanaryVerifier.CreateCanaryCiphertext(providerA, plaintext: expectedPlaintext);
            ciphertext.Should().NotBeNullOrWhiteSpace();

            // 2. Nạp lại provider A2 từ cùng thư mục keys A (mô phỏng sau khi unpack key ring restore)
            var servicesA2 = new ServiceCollection();
            servicesA2.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(tempKeysDirA))
                .SetApplicationName("Fitmate.Api");
            using var spA2 = servicesA2.BuildServiceProvider();
            var providerA2 = spA2.GetRequiredService<IDataProtectionProvider>();

            var (success, error) = DataProtectionCanaryVerifier.VerifyCanaryDecryption(
                providerA2,
                ciphertext,
                expectedPlaintext: expectedPlaintext);

            success.Should().BeTrue();
            error.Should().BeNull();

            // 3. Nạp provider B từ thư mục keys B khác (mô phỏng mất key hoặc key ring bị hỏng)
            var servicesB = new ServiceCollection();
            servicesB.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(tempKeysDirB))
                .SetApplicationName("Fitmate.Api");
            using var spB = servicesB.BuildServiceProvider();
            var providerB = spB.GetRequiredService<IDataProtectionProvider>();

            var (successB, errorB) = DataProtectionCanaryVerifier.VerifyCanaryDecryption(
                providerB,
                ciphertext,
                expectedPlaintext: expectedPlaintext);

            successB.Should().BeFalse();
            errorB.Should().Contain("cryptographic error");
        }
        finally
        {
            if (Directory.Exists(tempKeysDirA)) Directory.Delete(tempKeysDirA, true);
            if (Directory.Exists(tempKeysDirB)) Directory.Delete(tempKeysDirB, true);
        }
    }
}
