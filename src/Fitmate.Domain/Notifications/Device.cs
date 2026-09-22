using Fitmate.Domain.Common;

namespace Fitmate.Domain.Notifications;

/// <summary>
/// Push device registration. The token is stored encrypted with
/// ASP.NET Core DataProtection (purpose <c>fitmate.push-token.v1</c>) — see
/// DEC-040 in <c>docs/backend-release/decisions.md</c>. Only the ciphertext
/// plus the DataProtection key id are persisted; the raw token never touches
/// the database.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TokenHash"/> stores a SHA-256 hash of the raw token. It is used
/// only for deduplication of (UserId, TokenHash) — same token re-registered by
/// the same user updates <see cref="LastUsedAt"/> instead of creating a new
/// row. The hash is NOT used for sending push: sending requires decrypting
/// <see cref="EncryptedToken"/> via DataProtection with the persisted
/// <see cref="KeyId"/>.
/// </para>
/// <para>
/// A token can be active for several users at the same time (one device,
/// multiple accounts). The unique constraint is therefore scoped to
/// (UserId, TokenHash), not TokenHash alone.
/// </para>
/// </remarks>
public class Device : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hex of the raw device token. Used for dedup only.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>AES-GCM ciphertext of the raw token (DataProtection).</summary>
    public byte[] EncryptedToken { get; private set; } = null!;

    /// <summary>DataProtection key id used to encrypt this row. Supports rotation.</summary>
    public string KeyId { get; private set; } = null!;

    public DevicePlatform Platform { get; private set; }

    /// <summary>Push provider backend (FCM/Expo), independent from <see cref="Platform"/>.</summary>
    public PushProvider Provider { get; private set; }

    /// <summary>Soft revoke flag — set true when user/device logs out, swaps account, or rotates token.</summary>
    public bool IsActive { get; private set; } = true;

    public DateTime? RevokedAt { get; private set; }

    /// <summary>Updated by the worker after each successful send; used for sweep / liveness.</summary>
    public DateTime? LastUsedAt { get; private set; }

    private Device() { }

    /// <summary>
    /// Register a new device. Caller is responsible for hashing and encrypting
    /// the raw token. <paramref name="tokenHash"/> is SHA-256 hex.
    /// </summary>
    public static Device Create(
        Guid userId,
        string tokenHash,
        byte[] encryptedToken,
        string keyId,
        DevicePlatform platform,
        PushProvider provider)
    {
        if (userId == Guid.Empty) throw new ArgumentException("UserId must not be empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new ArgumentException("TokenHash must not be empty", nameof(tokenHash));
        if (encryptedToken is null || encryptedToken.Length == 0) throw new ArgumentException("EncryptedToken must not be empty", nameof(encryptedToken));
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("KeyId must not be empty", nameof(keyId));

        return new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            EncryptedToken = encryptedToken,
            KeyId = keyId,
            Platform = platform,
            Provider = provider,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Reuse existing row when the same (UserId, TokenHash) registers again.
    /// Updates the encrypted token (in case the token rotated), the key id,
    /// and the last-used timestamp.
    /// </summary>
    public void RefreshToken(byte[] encryptedToken, string keyId)
    {
        if (encryptedToken is null || encryptedToken.Length == 0) throw new ArgumentException("EncryptedToken must not be empty", nameof(encryptedToken));
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("KeyId must not be empty", nameof(keyId));
        EncryptedToken = encryptedToken;
        KeyId = keyId;
        LastUsedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Reactivate a previously revoked device row for the same user.
    /// Restores active state, clears revocation timestamp, and refreshes token/platform/provider.
    /// </summary>
    public void Reactivate(
        byte[] encryptedToken,
        string keyId,
        DevicePlatform? platform = null,
        PushProvider? provider = null)
    {
        if (encryptedToken is null || encryptedToken.Length == 0) throw new ArgumentException("EncryptedToken must not be empty", nameof(encryptedToken));
        if (string.IsNullOrWhiteSpace(keyId)) throw new ArgumentException("KeyId must not be empty", nameof(keyId));
        EncryptedToken = encryptedToken;
        KeyId = keyId;
        if (platform.HasValue) Platform = platform.Value;
        if (provider.HasValue) Provider = provider.Value;
        IsActive = true;
        RevokedAt = null;
        LastUsedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>Mark the row as last-used. Idempotent.</summary>
    public void MarkLastUsed()
    {
        LastUsedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Soft revoke. Idempotent. After revoke the worker must not target this
    /// device. Push deliveries still in flight are marked FailedPermanent
    /// with reason <c>device_inactive</c> by the sender-check guard.
    /// </summary>
    public void Revoke()
    {
        if (!IsActive) return;
        IsActive = false;
        RevokedAt = DateTime.UtcNow;
        Touch();
    }
}
