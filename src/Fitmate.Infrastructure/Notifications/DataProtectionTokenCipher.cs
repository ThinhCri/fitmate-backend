using Fitmate.Application.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>
/// ASP.NET Core DataProtection-based implementation of <see cref="IPushDeliveryTokens"/>.
///
/// <para>
/// Key ring: persisted via the key management options registered in Program.cs
/// (typically <c>PersistKeysToFileSystem</c> pointing to <c>./keys</c> or a
/// secret-manager path). The key ring directory is NOT committed to git.
/// </para>
///
/// <para>
/// Logging: NEVER log the raw token, ciphertext, or key material.
/// </para>
/// </summary>
public class DataProtectionTokenCipher : IPushDeliveryTokens
{
    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionTokenCipher> _logger;

    private const string Purpose = "fitmate.push-token.v1";

    public DataProtectionTokenCipher(
        IDataProtectionProvider provider,
        ILogger<DataProtectionTokenCipher> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public byte[] Protect(string rawToken)
    {
        ArgumentNullException.ThrowIfNull(rawToken);
        var plaintext = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var protectedBytes = _protector.Protect(plaintext);
        _logger.LogDebug("Push token encrypted successfully");
        return protectedBytes;
    }

    public string Unprotect(byte[] encryptedToken)
    {
        ArgumentNullException.ThrowIfNull(encryptedToken);
        try
        {
            var plaintext = _protector.Unprotect(encryptedToken);
            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push token decryption failed; key may have been rotated or payload corrupted.");
            throw;
        }
    }
}
