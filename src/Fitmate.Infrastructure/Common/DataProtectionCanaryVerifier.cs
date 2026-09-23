using System.Security.Cryptography;
using Fitmate.Application.Common;
using Microsoft.AspNetCore.DataProtection;

namespace Fitmate.Infrastructure.Common;

/// <summary>
/// BE-825: Bộ kiểm chứng DataProtection Canary Token cho kịch bản phục hồi thảm họa.
/// Thực hiện mã hóa canary token trước khi sao lưu và giải mã kiểm chứng sau khi khôi phục key ring.
/// </summary>
public static class DataProtectionCanaryVerifier
{
    /// <summary>
    /// Tạo canary token ciphertext bằng IDataProtectionProvider hiện tại.
    /// </summary>
    public static string CreateCanaryCiphertext(
        IDataProtectionProvider provider,
        string purpose = PostRestoreInvariantChecker.DefaultCanaryPurpose,
        string plaintext = PostRestoreInvariantChecker.DefaultCanaryExpectedPlaintext)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var protector = provider.CreateProtector(purpose);
        return protector.Protect(plaintext);
    }

    /// <summary>
    /// Xác minh giải mã canary token bằng IDataProtectionProvider sau khi restore.
    /// Trả về true nếu giải mã thành công và plaintext khớp 100%.
    /// </summary>
    public static (bool Success, string? ErrorMessage) VerifyCanaryDecryption(
        IDataProtectionProvider provider,
        string ciphertext,
        string purpose = PostRestoreInvariantChecker.DefaultCanaryPurpose,
        string expectedPlaintext = PostRestoreInvariantChecker.DefaultCanaryExpectedPlaintext)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);

        try
        {
            var protector = provider.CreateProtector(purpose);
            var decrypted = protector.Unprotect(ciphertext);
            if (!string.Equals(decrypted, expectedPlaintext, StringComparison.Ordinal))
            {
                return (false, $"Canary plaintext mismatch: expected '{expectedPlaintext}', got '{decrypted}'.");
            }

            return (true, null);
        }
        catch (CryptographicException ex)
        {
            return (false, $"Canary decryption failed due to cryptographic error (missing or altered key ring): {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error during canary decryption: {ex.Message}");
        }
    }
}

