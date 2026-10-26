using Fitmate.Application.Notifications;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-200: PushDeliveryDispatcher.DispatchAsync.
/// Skip behaviour: disabled config OR no active device → no backlog, audit only.
/// Fan-out: enabled + devices → 1 Notification + N PushDelivery + 1 outbox event.
/// Idempotency: same source tuple twice → second call short-circuits.
/// </summary>
public class PushDeliveryDispatcherTests
{
    private sealed class InMemoryNotificationStore : INotificationStore
    {
        public Dictionary<Guid, Notification> ById { get; } = new();
        public List<Notification> Added { get; } = new();
        public Task<(IReadOnlyList<Notification>, int)> ListMinePagedAsync(Guid u, bool? r, int p, int ps, CancellationToken ct) => throw new NotImplementedException();
        public Task<Notification?> FindByIdForOwnerAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotImplementedException();
        public Task MarkReadAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Notification n, CancellationToken ct)
        {
            Added.Add(n);
            ById[n.Id] = n;
            return Task.CompletedTask;
        }
        public Task<bool> ExistsBySourceAsync(Guid userId, Guid sourceEventId, string sourceType, CancellationToken ct)
            => Task.FromResult(ById.Values.Any(n => n.UserId == userId && n.SourceEventId == sourceEventId && n.SourceType == sourceType));
    }

    private sealed class InMemoryDeviceStore : IDeviceStore
    {
        public Task<T> WithTokenLockAsync<T>(string tokenHash, Func<Task<T>> action, CancellationToken ct) => action();
        public List<Device> Devices { get; } = new();
        public Task<Device?> FindByTokenHashAsync(Guid userId, string tokenHash, CancellationToken ct) => throw new NotImplementedException();
        public Task<Device?> FindByIdForOwnerAsync(Guid deviceId, Guid userId, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Device d, CancellationToken ct) { Devices.Add(d); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<Device>> GetActiveDevicesAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(Devices.Where(d => d.UserId == userId && d.IsActive).ToList());
        public Task<IReadOnlyList<Device>> FindActiveDevicesByTokenHashAsync(string tokenHash, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(Devices.Where(d => d.TokenHash == tokenHash && d.IsActive).ToList());
    }

    private sealed class InMemoryPushStore : IPushStore
    {
        public Task<bool> TryClaimDeliveryAsync(Guid deliveryId, CancellationToken ct) => Task.FromResult(true);
        public async Task<IReadOnlyList<PushDelivery>> FetchDueForNotificationAsync(Guid notificationId, CancellationToken ct) =>
            (await FetchDueBatchAsync(1000, ct)).Where(d => d.NotificationId == notificationId).ToList();
        public List<PushDelivery> Added { get; } = new();
        public Task AddAsync(PushDelivery d, CancellationToken ct) { Added.Add(d); return Task.CompletedTask; }
        public Task MarkSentAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task UpdateStatusAsync(Guid id, PushDeliveryStatus s, string? e, DateTime? n, DateTime? sa, CancellationToken ct) => throw new NotImplementedException();
        public Task<PushDelivery?> GetByIdAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<PushDelivery>> FetchDueBatchAsync(int bs, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> ClaimExpiredBatchAsync(int bs, CancellationToken ct) => throw new NotImplementedException();
        public Task<PushProviderConfig?> GetConfigAsync(CancellationToken ct)
            => Task.FromResult(Config);
        public PushProviderConfig? Config { get; set; }
    }

    private sealed class CapturingOutbox : IOutboxDispatcher
    {
        public List<(string Type, string Payload)> Enqueued { get; } = new();
        public void Enqueue(string type, object payload) { Enqueued.Add((type, payload?.ToString() ?? "")); }
    }

    private sealed class CapturingAudit : INotificationAuditLogger
    {
        public List<(string Action, Guid UserId, IReadOnlyDictionary<string, string> Meta)> Entries { get; } = new();
        public void Log(string action, Guid userId, IReadOnlyDictionary<string, string> metadata)
            => Entries.Add((action, userId, metadata));
    }

    private sealed class StubCipher : IPushDeliveryTokens
    {
        public byte[] Protect(string raw) => System.Text.Encoding.UTF8.GetBytes($"enc:{raw}");
        public string Unprotect(byte[] enc) => System.Text.Encoding.UTF8.GetString(enc)["enc:".Length..];
    }

    private static Device NewDevice(Guid userId, bool active = true)
    {
        var d = Device.Create(userId, $"hash-{Guid.NewGuid()}", System.Text.Encoding.UTF8.GetBytes("enc:tok"), "k1", DevicePlatform.Android, PushProvider.Fcm);
        if (!active) d.Revoke();
        return d;
    }

    internal static class _TapRemoved { }

    private static NotificationPayload NewPayload(Guid recipient, string sourceType, Guid? sourceEventId = null)
        => new(recipient, NotificationType.FriendRequest, "Title", null, new { requestId = sourceEventId ?? Guid.NewGuid(), senderId = Guid.NewGuid() }, sourceEventId ?? Guid.NewGuid(), sourceType);

    [Fact]
    public async Task Dispatch_DisabledConfig_CreatesNotificationOnly()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore { Config = PushProviderConfig.Disabled() };
        var ob = new CapturingOutbox();
        var audit = new CapturingAudit();
        var dispatcher = new PushDeliveryDispatcher(ns, ds, ps, ob, audit);

        var id = await dispatcher.DispatchAsync(NewPayload(Guid.NewGuid(), "friend_request_sent"));

        id.Should().NotBe(Guid.Empty);
        ns.Added.Should().HaveCount(1);
        ps.Added.Should().BeEmpty(); // no delivery rows
        ob.Enqueued.Should().BeEmpty(); // no outbox event
        audit.Entries.Should().ContainSingle(e => e.Action == "notification.skipped" && e.Meta["reason"] == "push_disabled");
    }

    [Fact]
    public async Task Dispatch_NoConfig_CreatesNotificationOnly()
    {
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        var ps = new InMemoryPushStore { Config = null };
        var ob = new CapturingOutbox();
        var audit = new CapturingAudit();
        var dispatcher = new PushDeliveryDispatcher(ns, ds, ps, ob, audit);

        await dispatcher.DispatchAsync(NewPayload(Guid.NewGuid(), "friend_request_sent"));

        ns.Added.Should().HaveCount(1);
        ps.Added.Should().BeEmpty();
        audit.Entries.Should().ContainSingle(e => e.Meta["reason"] == "push_disabled");
    }

    [Fact]
    public async Task Dispatch_EnabledButNoDevice_CreatesNotificationOnly()
    {
        var userId = Guid.NewGuid();
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore(); // no devices
        var cfg = PushProviderConfig.Disabled();
        cfg.Enable(PushProvider.Fcm, "{}");
        var ps = new InMemoryPushStore { Config = cfg };
        var ob = new CapturingOutbox();
        var audit = new CapturingAudit();
        var dispatcher = new PushDeliveryDispatcher(ns, ds, ps, ob, audit);

        await dispatcher.DispatchAsync(NewPayload(userId, "friend_request_sent"));

        ns.Added.Should().HaveCount(1);
        ps.Added.Should().BeEmpty();
        ob.Enqueued.Should().BeEmpty();
        audit.Entries.Should().ContainSingle(e => e.Meta["reason"] == "no_active_device");
    }

    [Fact]
    public async Task Dispatch_EnabledWithDevices_FansOutPerDevice()
    {
        var userId = Guid.NewGuid();
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        ds.Devices.Add(NewDevice(userId, active: true));
        ds.Devices.Add(NewDevice(userId, active: true));
        ds.Devices.Add(NewDevice(userId, active: false)); // inactive, must be skipped
        var cfg = PushProviderConfig.Disabled();
        cfg.Enable(PushProvider.Fcm, "{}");
        var ps = new InMemoryPushStore { Config = cfg };
        var ob = new CapturingOutbox();
        var audit = new CapturingAudit();
        var dispatcher = new PushDeliveryDispatcher(ns, ds, ps, ob, audit);

        await dispatcher.DispatchAsync(NewPayload(userId, "friend_request_sent"));

        ns.Added.Should().HaveCount(1);
        ps.Added.Should().HaveCount(2); // 2 active devices
        ob.Enqueued.Should().ContainSingle(e => e.Type == "notification.push_requested");
        audit.Entries.Should().BeEmpty(); // no skipped audit when fully fanned out
    }

    [Fact]
    public async Task Dispatch_DuplicateSource_IsIdempotent()
    {
        var userId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var ns = new InMemoryNotificationStore();
        var ds = new InMemoryDeviceStore();
        ds.Devices.Add(NewDevice(userId));
        var cfg = PushProviderConfig.Disabled();
        cfg.Enable(PushProvider.Fcm, "{}");
        var ps = new InMemoryPushStore { Config = cfg };
        var ob = new CapturingOutbox();
        var audit = new CapturingAudit();
        var dispatcher = new PushDeliveryDispatcher(ns, ds, ps, ob, audit);

        var first = await dispatcher.DispatchAsync(NewPayload(userId, "friend_request_sent", sourceEventId));
        var second = await dispatcher.DispatchAsync(NewPayload(userId, "friend_request_sent", sourceEventId));

        first.Should().NotBe(Guid.Empty);
        second.Should().Be(Guid.Empty); // second call short-circuits
        ns.Added.Should().HaveCount(1);
        ps.Added.Should().HaveCount(1);
        ob.Enqueued.Should().HaveCount(1);
    }
}
