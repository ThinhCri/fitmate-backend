using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Pure Application abstraction for push delivery rows and the provider config.
/// </summary>
public interface IPushStore
{
    Task<bool> TryClaimDeliveryAsync(Guid deliveryId, CancellationToken ct);
    Task<IReadOnlyList<PushDelivery>> FetchDueForNotificationAsync(Guid notificationId, CancellationToken ct);
    /// <summary>Add a push delivery row.</summary>
    Task AddAsync(PushDelivery delivery, CancellationToken ct = default);

    /// <summary>Mark a delivery as sent.</summary>
    Task MarkSentAsync(Guid deliveryId, CancellationToken ct = default);

    /// <summary>Update delivery status and last error.</summary>
    Task UpdateStatusAsync(
        Guid deliveryId,
        PushDeliveryStatus status,
        string? lastError,
        DateTime? nextAttemptAt,
        DateTime? sentAt,
        CancellationToken ct = default);

    /// <summary>
    /// Load a delivery by id. Returns null if not found.
    /// </summary>
    Task<PushDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct = default);

    /// <summary>
    /// Fetch a batch of pending / deferred / retryable deliveries for processing.
    /// </summary>
    Task<IReadOnlyList<PushDelivery>> FetchDueBatchAsync(
        int batchSize,
        CancellationToken ct = default);

    /// <summary>
    /// Claim expired deliveries for the sweep job using SKIP LOCKED.
    /// </summary>
    Task<int> ClaimExpiredBatchAsync(int batchSize, CancellationToken ct = default);

    /// <summary>
    /// Get the singleton push provider config. Returns null if not yet seeded.
    /// </summary>
    Task<PushProviderConfig?> GetConfigAsync(CancellationToken ct = default);
}
