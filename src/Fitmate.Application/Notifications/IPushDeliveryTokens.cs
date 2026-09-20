namespace Fitmate.Application.Notifications;

/// <summary>
/// Token encryption abstraction. The Application interface stays
/// implementation-free so Infrastructure controls what persistence layer is
/// used for the key ring (file system, secret manager, ...). See DEC-040.
/// </summary>
public interface IPushDeliveryTokens
{
    /// <summary>
    /// Encrypt a raw token. Returns the ciphertext and the DataProtection key
    /// id used to encrypt it (so rotation can be detected later).
    /// </summary>
    (byte[] EncryptedToken, string KeyId) Protect(string rawToken);

    /// <summary>
    /// Encrypt a raw token preferring the given key id. Used when refreshing
    /// an existing row so the stored ciphertext stays under the same key.
    /// </summary>
    (byte[] EncryptedToken, string KeyId) ProtectWithKey(string rawToken, string preferredKeyId);

    /// <summary>
    /// Decrypt a previously protected token. Throws if the key ring no longer
    /// contains the corresponding key — caller must treat as
    /// <see cref="Domain.PushDeliveryStatus.TokenInvalid"/>.
    /// </summary>
    string Unprotect(byte[] encryptedToken, string keyId);
}
