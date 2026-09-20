using System.Security.Cryptography;
using System.Text;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Default <see cref="IDeviceService"/> impl.
/// Authorization is enforced by always scoping queries to the passed
/// <c>userId</c>. Cross-user access returns 404.
/// </summary>
/// <remarks>
/// Encryption: SHA-256 hex for dedup lookup; ASP.NET Core DataProtection (wired
/// in Infrastructure) handles the encrypt / decrypt of the raw token before
/// persistence. See <see cref="IPushDeliveryTokens"/>.
/// </remarks>
public class DeviceService : IDeviceService
{
    private const int MinTokenLength = 32;
    private const int MaxTokenLength = 4096;
    private const string TokenCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-:.=+/";

    private readonly IDeviceStore _store;
    private readonly IPushDeliveryTokens _cipher;

    public DeviceService(IDeviceStore store, IPushDeliveryTokens cipher)
    {
        _store = store;
        _cipher = cipher;
    }

    public async Task<RegisterDeviceResult> RegisterAsync(
        Guid userId,
        string token,
        DevicePlatform platform,
        PushProvider provider,
        CancellationToken ct = default)
    {
        ValidateUser(userId);
        ValidateToken(token);
        // platform/provider are validated by the controller (string ↔ enum)
        // before reaching this service.

        var tokenHash = ComputeTokenHash(token);
        var existing = await _store.FindByTokenHashAsync(userId, tokenHash, ct);

        if (existing is not null)
        {
            // Idempotent refresh: update encrypted token (in case of rotation), key id, last_used_at.
            var encryptedToken = _cipher.Protect(token);
            existing.RefreshToken(encryptedToken, existing.KeyId);
            await _store.SaveChangesAsync(ct);
            return new RegisterDeviceResult(existing.Id, DateTime.UtcNow);
        }

        var newEncryptedToken = _cipher.Protect(token);
        // KeyId is stored purely as a diagnostic stamp. DataProtection embeds the key
        // selector in the ciphertext format, so unprotect works without the stored keyId.
        // Use a sentinel value.
        var sentinelKeyId = "dataprotection";
        var device = Device.Create(
            userId,
            tokenHash,
            newEncryptedToken,
            sentinelKeyId,
            platform,
            provider);

        await _store.AddAsync(device, ct);
        await _store.SaveChangesAsync(ct);
        // KeyId is stored purely as a diagnostic stamp; ciphertext carries the key
        // selector automatically in DataProtection format. Use a sentinel value.
        return new RegisterDeviceResult(device.Id, DateTime.UtcNow);
    }

    public async Task RevokeAsync(Guid userId, Guid deviceId, CancellationToken ct = default)
    {
        ValidateUser(userId);
        if (deviceId == Guid.Empty)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "deviceId must not be empty",
                new Dictionary<string, string[]> { ["deviceId"] = new[] { "empty" } });

        var device = await _store.FindByIdForOwnerAsync(deviceId, userId, ct);
        if (device is null)
        {
            // Unknown id OR wrong owner → 404 (do not leak existence).
            throw new NotFoundException(
                ErrorCodes.General.NotFound,
                $"Device '{deviceId}' not found.");
        }

        device.Revoke(); // idempotent
        await _store.SaveChangesAsync(ct);
    }

    private static string ComputeTokenHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static void ValidateUser(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new UnauthorizedException(
                ErrorCodes.Identity.NotAuthenticated,
                "User is not authenticated.");
    }

    private static void ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token))
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "device_token is required",
                new Dictionary<string, string[]> { ["device_token"] = new[] { "empty" } });
        if (token.Length < MinTokenLength || token.Length > MaxTokenLength)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                $"device_token length must be {MinTokenLength}..{MaxTokenLength}",
                new Dictionary<string, string[]> { ["device_token"] = new[] { $"out of range {MinTokenLength}..{MaxTokenLength}" } });
        foreach (var c in token)
        {
            if (TokenCharset.IndexOf(c) < 0)
                throw new ValidationException(
                    ErrorCodes.Validation.FieldInvalid,
                    "device_token contains invalid characters",
                    new Dictionary<string, string[]> { ["device_token"] = new[] { "invalid charset" } });
        }
    }
}
