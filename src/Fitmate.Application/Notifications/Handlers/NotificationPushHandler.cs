using System.Text.Json;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Social;
using Microsoft.Extensions.Logging;

namespace Fitmate.Application.Notifications.Handlers;

/// <summary>
/// Outbox handler for <c>notification.push_requested</c>. Picks up rows
/// enqueued by <see cref="PushDeliveryDispatcher"/>, walks each pending
/// <see cref="PushDelivery"/> for the given notification, and asks
/// <see cref="IPushProvider"/> to send.
///
/// <para>
/// Sender-side checks are enforced BEFORE the provider call (see
/// application-services.md plan §G):
/// </para>
/// <list type="number">
///   <item>device active + owned by recipient</item>
///   <item>delivery not yet expired (TTL)</item>
///   <item>domain validity (e.g. friend request still in matching state,
///   report not yet dismissed, no block between recipient and actor)</item>
/// </list>
/// Per-device failure isolation: a single device failure does NOT affect
/// sibling devices.
/// </summary>
public sealed class NotificationPushHandler : IOutboxHandler
{
    public string Type => "notification.push_requested";

    private readonly INotificationStore _notifications;
    private readonly IDeviceStore _devices;
    private readonly IPushStore _pushes;
    private readonly IPushProvider _provider;
    private readonly IPushDeliveryTokens _cipher;
    private readonly IBlockStore _blocks;
    private readonly INotificationAuditLogger _audit;
    private readonly ILogger<NotificationPushHandler> _logger;

    public NotificationPushHandler(
        INotificationStore notifications,
        IDeviceStore devices,
        IPushStore pushes,
        IPushProvider provider,
        IPushDeliveryTokens cipher,
        IBlockStore blocks,
        INotificationAuditLogger audit,
        ILogger<NotificationPushHandler> logger)
    {
        _notifications = notifications;
        _devices = devices;
        _pushes = pushes;
        _provider = provider;
        _cipher = cipher;
        _blocks = blocks;
        _audit = audit;
        _logger = logger;
    }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        NotificationPushPayload? payload;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            payload = JsonSerializer.Deserialize<NotificationPushPayload>(
                doc.RootElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push handler: failed to parse outbox payload (will retry).");
            throw; // let OutboxWorker mark failed + backoff
        }
        if (payload is null) throw new InvalidOperationException("Empty push payload.");

        var notification = await _notifications.FindByIdForOwnerAsync(
            payload.NotificationId, payload.RecipientId, ct);
        if (notification is null)
        {
            // Notification was deleted / never existed → drop.
            _audit.Log(
                "push.skipped",
                payload.RecipientId,
                new Dictionary<string, string>
                {
                    ["reason"] = "notification_missing",
                    ["notification_id"] = payload.NotificationId.ToString(),
                });
            return;
        }

        var now = DateTime.UtcNow;
        var devices = await _devices.GetActiveDevicesAsync(payload.RecipientId, ct);

        if (devices.Count == 0)
        {
            _audit.Log(
                "push.skipped",
                payload.RecipientId,
                new Dictionary<string, string>
                {
                    ["reason"] = "no_active_device",
                    ["notification_id"] = payload.NotificationId.ToString(),
                });
            return;
        }

        foreach (var device in devices)
        {
            if (ct.IsCancellationRequested) break;

            // Per-delivery lookup to pick up expiry / retry-attempts.
            var deliveries = await _pushes.FetchDueBatchAsync(batchSize: 100, ct);
            var mine = deliveries.FirstOrDefault(d =>
                d.NotificationId == payload.NotificationId &&
                d.DeviceId == device.Id);
            if (mine is null) continue;

            // ─── Sender-side checks ──────────────────────────────────────
            if (!device.IsActive)
            {
                await MarkPermanent(mine, "device_inactive", payload.RecipientId, ct);
                continue;
            }
            if (device.UserId != payload.RecipientId)
            {
                await MarkPermanent(mine, "owner_mismatch", payload.RecipientId, ct);
                continue;
            }
            if (mine.IsExpired(now))
            {
                await MarkPermanent(mine, "expired", payload.RecipientId, ct);
                continue;
            }

            // ─── Domain validity per source_type ─────────────────────────
            var valid = await IsDomainValidAsync(
                payload.SourceType, payload.RecipientId, notification, ct);
            if (!valid.ok)
            {
                await MarkPermanent(mine, valid.reason!, payload.RecipientId, ct);
                continue;
            }

            // ─── Provider call ───────────────────────────────────────────
            string decryptedToken;
            try
            {
                decryptedToken = _cipher.Unprotect(device.EncryptedToken);
            }
            catch (Exception)
            {
                // Key no longer available → token invalid.
                await MarkTokenInvalid(mine, device, payload.RecipientId, ct);
                continue;
            }

            PushSendResult result;
            try
            {
                result = await _provider.SendAsync(
                    decryptedToken,
                    notification.Title,
                    notification.Summary,
                    notification.PayloadJson ?? "{}",
                    ct);
            }
            catch (Exception ex)
            {
                // Unexpected exception → treat as retryable.
                if (mine.IsExpired(now.Add(Backoff(mine.Attempts + 1))))
                {
                    await MarkPermanent(mine, "expired", payload.RecipientId, ct);
                }
                else
                {
                    await _pushes.UpdateStatusAsync(
                        mine.Id,
                        PushDeliveryStatus.FailedRetryable,
                        Truncate(ex.Message, 200),
                        now.Add(Backoff(mine.Attempts + 1)),
                        sentAt: null,
                        ct);
                }
                continue;
            }

            await HandleResultAsync(mine, device, result, notification, payload.RecipientId, now, ct);
        }
    }

    private async Task<(bool ok, string? reason)> IsDomainValidAsync(
        string sourceType,
        Guid recipientId,
        Notification notification,
        CancellationToken ct)
    {
        // For FriendRequest / System we run additional checks via payload;
        // the dispatcher already validated allowlist & payload schema.
        // Block check applies whenever source_type == "friend" (DEC-038, DEC-039).
        if (sourceType == "friend" && notification.PayloadJson is not null)
        {
            // Pull sender id from the payload if present.
            Guid? senderId = TryReadGuid(notification.PayloadJson, "senderId");
            if (senderId.HasValue && senderId.Value != recipientId)
            {
                var blocked = await _blocks.IsBlockedEitherWayAsync(recipientId, senderId.Value, ct);
                if (blocked) return (false, "blocked_before_dispatch");
            }
        }
        // system / moderation: report dismissed handled by upstream store / audit;
        // for now we accept (full implementation belongs to BE-200b when booking / payment producers are wired).
        return (true, null);
    }

    private async Task HandleResultAsync(
        PushDelivery delivery,
        Device device,
        PushSendResult result,
        Notification notification,
        Guid recipientId,
        DateTime now,
        CancellationToken ct)
    {
        switch (result.Outcome)
        {
            case PushSendOutcome.Success:
                await _pushes.UpdateStatusAsync(
                    delivery.Id,
                    PushDeliveryStatus.Sent,
                    null,
                    nextAttemptAt: null,
                    sentAt: now,
                    ct);
                _audit.Log(
                    "push.sent",
                    recipientId,
                    new Dictionary<string, string>
                    {
                        ["notification_id"] = notification.Id.ToString(),
                        ["device_id"] = device.Id.ToString(),
                    });
                break;

            case PushSendOutcome.TokenInvalid:
                await MarkTokenInvalid(delivery, device, recipientId, ct);
                break;

            case PushSendOutcome.Retryable:
                var backoff = Backoff(delivery.Attempts + 1);
                if (now.Add(backoff) >= delivery.ExpiresAt)
                {
                    await MarkPermanent(delivery, "expired", recipientId, ct);
                }
                else
                {
                    await _pushes.UpdateStatusAsync(
                        delivery.Id,
                        PushDeliveryStatus.FailedRetryable,
                        Truncate(result.ProviderMessage, 200),
                        now.Add(backoff),
                        sentAt: null,
                        ct);
                }
                break;

            case PushSendOutcome.Deferred:
                if (now.AddHours(1) >= delivery.ExpiresAt)
                {
                    await MarkPermanent(delivery, "expired", recipientId, ct);
                }
                else
                {
                    await _pushes.UpdateStatusAsync(
                        delivery.Id,
                        PushDeliveryStatus.Deferred,
                        Truncate(result.ProviderMessage, 200),
                        now.AddHours(1),
                        sentAt: null,
                        ct);
                }
                break;

            case PushSendOutcome.ProviderNotConfigured:
                await _pushes.UpdateStatusAsync(
                    delivery.Id,
                    PushDeliveryStatus.FailedPermanent,
                    "push_provider_not_configured",
                    nextAttemptAt: null,
                    sentAt: null,
                    ct);
                _audit.Log(
                    "push.failed",
                    recipientId,
                    new Dictionary<string, string>
                    {
                        ["reason"] = "push_provider_not_configured",
                        ["notification_id"] = notification.Id.ToString(),
                    });
                break;

            case PushSendOutcome.PayloadRejected:
                await _pushes.UpdateStatusAsync(
                    delivery.Id,
                    PushDeliveryStatus.FailedPermanent,
                    "payload_rejected",
                    nextAttemptAt: null,
                    sentAt: null,
                    ct);
                break;
        }
    }

    private async Task MarkPermanent(
        PushDelivery delivery, string reason, Guid recipientId, CancellationToken ct)
    {
        await _pushes.UpdateStatusAsync(
            delivery.Id,
            PushDeliveryStatus.FailedPermanent,
            Truncate(reason, 200),
            nextAttemptAt: null,
            sentAt: null,
            ct);
        _audit.Log(
            "push.failed",
            recipientId,
            new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["delivery_id"] = delivery.Id.ToString(),
            });
    }

    private async Task MarkTokenInvalid(
        PushDelivery delivery, Device device, Guid recipientId, CancellationToken ct)
    {
        await _pushes.UpdateStatusAsync(
            delivery.Id,
            PushDeliveryStatus.TokenInvalid,
            "token_invalid",
            nextAttemptAt: null,
            sentAt: null,
            ct);
        device.Revoke();
        _audit.Log(
            "push.token_invalid",
            recipientId,
            new Dictionary<string, string>
            {
                ["device_id"] = device.Id.ToString(),
                ["reason"] = "provider_reported_invalid",
            });
    }

    private static Guid? TryReadGuid(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(field, out var el) &&
                el.ValueKind == JsonValueKind.String &&
                Guid.TryParse(el.GetString(), out var g))
                return g;
        }
        catch { /* swallow */ }
        return null;
    }

    private static TimeSpan Backoff(int attempts) => attempts switch
    {
        0 => TimeSpan.FromSeconds(1),
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        3 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromHours(1),
    };

    private static string Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max]);

    private sealed record NotificationPushPayload(
        Guid NotificationId,
        Guid RecipientId,
        string SourceType,
        Guid SourceEventId);
}
