using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application abstraction for device persistence.
/// </summary>
public interface IDeviceStore
{
    /// <summary>
    /// Find a device by (userId, tokenHash). Returns null if not found.
    /// </summary>
    Task<Device?> FindByTokenHashAsync(Guid userId, string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Find a device by id and user id. Returns null if missing or wrong owner.
    /// </summary>
    Task<Device?> FindByIdForOwnerAsync(Guid deviceId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Add a new device row.
    /// </summary>
    Task AddAsync(Device device, CancellationToken ct = default);

    /// <summary>
    /// SaveChanges for the current DbContext (commits the transaction).
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Get all active devices for a user. Used by the outbox handler to fan out
    /// push delivery.
    /// </summary>
    Task<IReadOnlyList<Device>> GetActiveDevicesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Find all active devices across all users that match the given tokenHash.
    /// Used during device registration to revoke previously active registrations
    /// of the same physical device/token on other accounts (cross-account push leak prevention).
    /// </summary>
    Task<IReadOnlyList<Device>> FindActiveDevicesByTokenHashAsync(string tokenHash, CancellationToken ct = default);
}
