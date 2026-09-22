using System.Text.Json;
using Fitmate.Application.Notifications;
using Fitmate.Application.Notifications.Handlers;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Social;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-200: NotificationPushHandler - outbox worker for notification.push_requested.
/// Per-device failure isolation. Domain validity (block check for friend source).
/// </summary>
public class NotificationPushHandlerTests
{
    // Mock stores

    private sealed class InMemoryNotificationStore : INotificationStore
    {
        public Dictionary<Guid, Notification> ById { get; } = new();
        public Task<(IReadOnlyList<Notification>, int)> ListMinePagedAsync(Guid u, bool? r, int p, int ps, CancellationToken ct)
            => Task.FromResult<(IReadOnlyList<Notification>, int)>((new List<Notification>(), 0));
        public Task<Notification?> FindByIdForOwnerAsync(Guid id, Guid userId, CancellationToken ct)
            => Task.FromResult(ById.TryGetValue(id, out var n) && n.UserId == userId ? n : null);
        public Task MarkReadAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task AddAsync(Notification n, CancellationToken ct) { ById[n.Id] = n; return Task.CompletedTask; }
        public Task<bool> ExistsBySourceAsync(Guid u, Guid s, string t, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class InMemoryDeviceStore : IDeviceStore
    {
        public List<Device> Devices { get; } = new();
        public Task<Device?> FindByTokenHashAsync(Guid u, string t, CancellationToken ct) => Task.FromResult<Device?>(null);
        public Task<Device?> FindByIdForOwnerAsync(Guid d, Guid u, CancellationToken ct) => Task.FromResult<Device?>(null);
        public Task AddAsync(Device d, CancellationToken ct) { Devices.Add(d); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Device>> GetActiveDevicesAsync(Guid userId, CancellationToken ct)
            // Return all devices to allow the handler's per-device checks (active,
            // owner match, expiry) to run. The handler enforces ownership, so a
            // device with mismatched UserId can reach the mismatch check.
            => Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());
        public Task<IReadOnlyList<Device>> FindActiveDevicesByTokenHashAsync(string tokenHash, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(Devices.Where(d => d.TokenHash == tokenHash && d.IsActive).ToList());
    }

    private sealed class InMemoryPushStore : IPushStore
    {
        public List<PushDelivery> Deliveries { get; } = new();
        public List<PushDelivery> DueBatch { get; set; } = new();
        public List<(Guid Id, PushDeliveryStatus Status, string? Error, DateTime? Next, DateTime? Sent)> Updates { get; } = new();
        public Task AddAsync(PushDelivery d, CancellationToken ct) { Deliveries.Add(d); return Task.CompletedTask; }
        public Task MarkSentAsync(Guid id, CancellationToken ct) { Updates.Add((id, PushDeliveryStatus.Sent, null, null, DateTime.UtcNow)); return Task.CompletedTask; }
        public Task UpdateStatusAsync(Guid id, PushDeliveryStatus s, string? e, DateTime? n, DateTime? sa, CancellationToken ct)
            { Updates.Add((id, s, e, n, sa)); return Task.CompletedTask; }
        public Task<PushDelivery?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Deliveries.FirstOrDefault(d => d.Id == id));
        public Task<IReadOnlyList<PushDelivery>> FetchDueBatchAsync(int bs, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PushDelivery>>(DueBatch);
        public Task<int> ClaimExpiredBatchAsync(int bs, CancellationToken ct) => Task.FromResult(DueBatch.Count);
        public Task<PushProviderConfig?> GetConfigAsync(CancellationToken ct) => Task.FromResult<PushProviderConfig?>(null);
    }

    private sealed class InMemoryBlockStore : IBlockStore
    {
        public HashSet<(Guid A, Guid B)> Blocks { get; } = new();
        public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct)
            => Task.FromResult(Blocks.Contains((a, b)) || Blocks.Contains((b, a)));
        public Task<bool> BlockExistsAsync(Guid blockerId, Guid blockedId, CancellationToken ct)
            => Task.FromResult(Blocks.Contains((blockerId, blockedId)));
        public Task AddBlockAsync(BlockedUser block, CancellationToken ct)
            { Blocks.Add((block.BlockerId, block.BlockedId)); return Task.CompletedTask; }
        public Task<bool> RemoveBlockAsync(Guid blockerId, Guid blockedId, CancellationToken ct)
        {
            var removed = Blocks.Remove((blockerId, blockedId));
            return Task.FromResult(removed);
        }
        public Task<(IReadOnlyList<Guid> BlockedIds, string? NextCursor)> ListBlockedIdsAsync(
            Guid blockerId, DateTime? cursorCreatedAt, Guid? cursorBlockedId, int limit, CancellationToken ct)
            => Task.FromResult<(IReadOnlyList<Guid>, string?)>(
                (BlockedIds(blockerId), null));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        private List<Guid> BlockedIds(Guid blockerId) => Blocks.Where(b => b.A == blockerId).Select(b => b.B).ToList();
    }

    private sealed class CapturingAudit : INotificationAuditLogger
    {
        public List<(string Action, Guid UserId, IReadOnlyDictionary<string, string> Meta)> Entries { get; } = new();
        public void Log(string action, Guid userId, IReadOnlyDictionary<string, string> metadata)
            => Entries.Add((action, userId, metadata));
    }

    private sealed class StubCipher : IPushDeliveryTokens
    {
        public byte[] Protect(string raw) => System.Text.Encoding.UTF8.GetBytes(raw);
        public string Unprotect(byte[] enc) => System.Text.Encoding.UTF8.GetString(enc);
    }

    // Helpers

    private static Device NewDevice(Guid userId, bool active = true)
    {
        var d = Device.Create(userId, $"hash-{Guid.NewGuid()}", System.Text.Encoding.UTF8.GetBytes("token"), "k1",
            DevicePlatform.Android, PushProvider.Fcm);
        if (!active) d.Revoke();
        return d;
    }

    private static Notification NewNotification(Guid recipientId, Guid sourceEventId, string sourceType, Guid? senderId = null)
    {
        string? payload = senderId.HasValue
            ? JsonSerializer.Serialize(new { requestId = sourceEventId, senderId = senderId.Value })
            : null;
        var n = Notification.Create(recipientId, NotificationType.FriendRequest,
            "Test Title", "Test Body",
            null, // summary
            payload,
            sourceEventId,
            sourceType);
        return n;
    }

    private static PushDelivery NewDelivery(Guid notificationId, Guid deviceId,
        DateTime? expiresAt = null, PushDeliveryStatus status = PushDeliveryStatus.Pending)
    {
        var exp = expiresAt ?? DateTime.UtcNow.AddHours(1);
        var d = exp <= DateTime.UtcNow
            ? PushDelivery.CreateExpired(notificationId, deviceId, exp)
            : PushDelivery.Create(notificationId, deviceId, exp);
        if (status == PushDeliveryStatus.Sweeping) d.MarkSweeping();
        return d;
    }

    private static NotificationPushHandler NewHandler(
        InMemoryNotificationStore ns,
        InMemoryDeviceStore ds,
        InMemoryPushStore ps,
        InMemoryBlockStore blocks,
        CapturingAudit audit,
        IPushProvider provider)
    {
        var logger = new Mock<ILogger<NotificationPushHandler>>();
        return new NotificationPushHandler(ns, ds, ps, provider, new StubCipher(), blocks, audit, logger.Object);
    }

    private static string PayloadJson(Guid notifId, Guid recipientId, string sourceType, Guid sourceEventId)
        => JsonSerializer.Serialize(new { NotificationId = notifId, RecipientId = recipientId, SourceType = sourceType, SourceEventId = sourceEventId });

    // Stub push provider

    private sealed class StubPushProvider : IPushProvider
    {
        private readonly PushSendResult _result;
        public StubPushProvider(PushSendResult result) => _result = result;
        public Task<PushSendResult> SendAsync(string deviceToken, string title, string? summary, string? payloadJson, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    // Tests

    [Fact]
    public async Task Handle_InvalidJson_ThrowsToTriggerRetry()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var act = () => handler.HandleAsync("not valid json {{{", CancellationToken.None);
        await act.Should().ThrowAsync<JsonException>();
    }

    [Fact]
    public async Task Handle_NotificationMissing_SkipsAndLogs()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var notifId = Guid.NewGuid();
        await handler.HandleAsync(PayloadJson(notifId, Guid.NewGuid(), "friend", Guid.NewGuid()), CancellationToken.None);

        audit.Entries.Should().ContainSingle(e => e.Action == "push.skipped" && e.Meta["reason"] == "notification_missing");
    }

    [Fact]
    public async Task Handle_NoActiveDevice_SkipsAndLogs()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore(); // no devices
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        audit.Entries.Should().ContainSingle(e => e.Action == "push.skipped" && e.Meta["reason"] == "no_active_device");
    }

    [Fact]
    public async Task Handle_DeviceInactive_MarksPermanent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId, active: false);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedPermanent);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.failed" && e.Meta["reason"] == "device_inactive");
    }

    [Fact]
    public async Task Handle_OwnerMismatch_MarksPermanent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        // Inject a device belonging to "otherId" so GetActiveDevicesAsync finds it
        // when called with the recipientId — the handler then detects owner mismatch.
        var device = NewDevice(otherId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedPermanent);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.failed" && e.Meta["reason"] == "owner_mismatch");
    }

    [Fact]
    public async Task Handle_ExpiredDelivery_MarksPermanent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id, expiresAt: DateTime.UtcNow.AddMinutes(-5)); // expired
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedPermanent);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.failed" && e.Meta["reason"] == "expired");
    }

    [Fact]
    public async Task Handle_FriendSource_BlockedPair_MarksPermanent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        // Block exists between recipient and sender
        await blocks.AddBlockAsync(BlockedUser.Create(recipientId, senderId), CancellationToken.None);

        var sourceEventId = Guid.NewGuid();
        var notif = NewNotification(recipientId, sourceEventId, "friend", senderId);
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", sourceEventId), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedPermanent);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.failed" && e.Meta["reason"] == "blocked_before_dispatch");
    }

    [Fact]
    public async Task Handle_ProviderSuccess_MarksSent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.Sent);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.sent");
    }

    [Fact]
    public async Task Handle_ProviderTokenInvalid_MarksTokenInvalid()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.TokenInvalid("invalid_token"));
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.TokenInvalid);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.token_invalid");
    }

    [Fact]
    public async Task Handle_ProviderRetryable_ReschedulesRetry()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Retryable("server busy"));
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id, expiresAt: DateTime.UtcNow.AddHours(2)); // far future
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedRetryable);
        ps.Updates[0].Next.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ProviderDeferred_ReschedulesOneHour()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Deferred("rate limited"));
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id, expiresAt: DateTime.UtcNow.AddHours(2));
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.Deferred);
    }

    [Fact]
    public async Task Handle_ProviderNotConfigured_MarksPermanent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.ProviderNotConfigured("no_provider"));
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedPermanent);
        audit.Entries.Should().ContainSingle(e => e.Action == "push.failed" && e.Meta["reason"] == "push_provider_not_configured");
    }

    [Fact]
    public async Task Handle_PayloadRejected_MarksPermanent()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.PayloadRejected("invalid payload"));
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery = NewDelivery(notif.Id, device.Id);
        ps.DueBatch.Add(delivery);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().ContainSingle(u => u.Id == delivery.Id && u.Status == PushDeliveryStatus.FailedPermanent);
    }

    [Fact]
    public async Task Handle_MultipleDevices_FansOutPerDevice()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device1 = NewDevice(recipientId);
        var device2 = NewDevice(recipientId);
        ds.Devices.Add(device1);
        ds.Devices.Add(device2);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        var delivery1 = NewDelivery(notif.Id, device1.Id);
        var delivery2 = NewDelivery(notif.Id, device2.Id);
        ps.DueBatch.Add(delivery1);
        ps.DueBatch.Add(delivery2);

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().HaveCount(2);
        ps.Updates.Should().OnlyContain(u => u.Status == PushDeliveryStatus.Sent);
        audit.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_DeliveryNotInBatch_SkipsDevice()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore();
        var blocks = new InMemoryBlockStore();
        var audit = new CapturingAudit();
        var provider = new StubPushProvider(PushSendResult.Success());
        var handler = NewHandler(ns, ds, ps, blocks, audit, provider);

        var recipientId = Guid.NewGuid();
        var device = NewDevice(recipientId);
        ds.Devices.Add(device);

        var notif = NewNotification(recipientId, Guid.NewGuid(), "friend");
        ns.ById[notif.Id] = notif;

        // No delivery in batch for this device
        ps.DueBatch.Add(NewDelivery(Guid.NewGuid(), device.Id)); // different notif

        await handler.HandleAsync(PayloadJson(notif.Id, recipientId, "friend", notif.SourceEventId!.Value), CancellationToken.None);

        ps.Updates.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }
}
