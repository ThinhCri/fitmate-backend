using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application interface for device registration and revocation.
/// Authorization (ownership) is enforced inside this service using the passed
/// <paramref name="userId"/>.
/// </summary>
public interface IDeviceService
{
    /// <summary>
    /// Register a device token for push delivery. If the same (userId, tokenHash)
    /// pair already exists, the row is refreshed (token rotated, last_used_at
    /// updated) instead of creating a duplicate — idempotent from the client's
    /// perspective.
    /// </summary>
    /// <remarks>
    /// The raw <paramref name="token"/> must already be validated by the caller
    /// (length 32..4096, charset). This service hashes and encrypts it before
    /// persisting.
    /// </remarks>
    Task<RegisterDeviceResult> RegisterAsync(
        Guid userId,
        string token,
        DevicePlatform platform,
        PushProvider provider,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-revoke a device registration so it stops receiving push notifications.
    /// Idempotent: returns 204 even if already revoked or unknown. Returns
    /// <c>null</c> if the device does not belong to the user (404 semantics).
    /// </summary>
    Task RevokeAsync(Guid userId, Guid deviceId, CancellationToken ct = default);
}
