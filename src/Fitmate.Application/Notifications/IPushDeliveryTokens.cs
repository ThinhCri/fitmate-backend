namespace Fitmate.Application.Notifications;

/// <summary>
/// Token encryption abstraction. The Application interface stays
/// implementation-free so Infrastructure controls what persistence layer is
/// used for the key ring (file system, secret manager, ...). See DEC-040.
/// </summary>
/// <remarks>
/// <para>
/// Per ASP.NET Core Data Protection semantics: the framework embeds the
/// active key id in the protected payload and resolves the right key on
/// unprotect, so the Application interface does NOT expose key selection or
/// key ids to callers. The <see cref="ProtectAndStampKey"/> /
/// <see cref="UnprotectAndCheckKey"/> methods exist purely to record the
/// "active key id at encrypt time" alongside the ciphertext so operators
/// can correlate rotation events after the fact. No caller can influence
/// which key is used.
/// </para>
/// </remarks>
public interface IPushDeliveryTokens
{
    /// <summary>
    /// Encrypt a raw token using the active ASP.NET Core Data Protection key.
    /// </summary>
    byte[] Protect(string rawToken);

    /// <summary>
    /// Decrypt a previously protected token. Throws if the key ring no longer
    /// contains the corresponding key — caller must treat as
    /// <see cref="Domain.PushDeliveryStatus.TokenInvalid"/>.
    /// </summary>
    string Unprotect(byte[] encryptedToken);
}
