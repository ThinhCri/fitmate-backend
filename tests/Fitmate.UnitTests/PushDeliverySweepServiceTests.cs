using Fitmate.Application.Notifications;
using Fitmate.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-200: PushDeliverySweepService — claim expired deliveries and mark them Expired.
/// Uses FOR UPDATE SKIP LOCKED semantics; per-row failure isolation.
/// </summary>
public class PushDeliverySweepServiceTests
{
    private sealed class InMemoryPushStore : IPushStore
    {
        public List<PushDelivery> All { get; } = new();
        public List<PushDelivery> ClaimedBatch { get; set; } = new();
        public List<(Guid Id, PushDeliveryStatus Status, string? Error, DateTime? Next, DateTime? Sent)> Updates { get; } = new();

        public Task AddAsync(PushDelivery d, CancellationToken ct) { All.Add(d); return Task.CompletedTask; }
        public Task MarkSentAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateStatusAsync(Guid id, PushDeliveryStatus s, string? e, DateTime? n, DateTime? sa, CancellationToken ct)
            { Updates.Add((id, s, e, n, sa)); return Task.CompletedTask; }
        public Task<PushDelivery?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(All.FirstOrDefault(d => d.Id == id));
        public Task<IReadOnlyList<PushDelivery>> FetchDueBatchAsync(int bs, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PushDelivery>>(ClaimedBatch);
        public Task<int> ClaimExpiredBatchAsync(int bs, CancellationToken ct)
            => Task.FromResult(ClaimedBatch.Count);
        public Task<PushProviderConfig?> GetConfigAsync(CancellationToken ct) => Task.FromResult<PushProviderConfig?>(null);
    }

    private sealed class CapturingAudit : INotificationAuditLogger
    {
        public List<(string Action, Guid UserId, IReadOnlyDictionary<string, string> Meta)> Entries { get; } = new();
        public void Log(string action, Guid userId, IReadOnlyDictionary<string, string> metadata)
            => Entries.Add((action, userId, metadata));
    }

    private static PushDelivery NewDelivery(
        DateTime expiresAt,
        PushDeliveryStatus status = PushDeliveryStatus.Sweeping)
    {
        // Use CreateExpired to bypass the "must be in the future" guard so that
        // sweep tests can simulate already-expired deliveries.
        var d = expiresAt <= DateTime.UtcNow
            ? PushDelivery.CreateExpired(Guid.NewGuid(), Guid.NewGuid(), expiresAt)
            : PushDelivery.Create(Guid.NewGuid(), Guid.NewGuid(), expiresAt);
        if (status == PushDeliveryStatus.Sweeping) d.MarkSweeping();
        if (status == PushDeliveryStatus.Sent) d.MarkSent();
        if (status == PushDeliveryStatus.Expired) { d.MarkSweeping(); d.MarkExpired(); }
        return d;
    }

    [Fact]
    public async Task SweepOnce_NothingToClaim_ReturnsZero()
    {
        var store = new InMemoryPushStore();
        var audit = new CapturingAudit();
        var svc = new PushDeliverySweepService(store, audit, Mock.Of<ILogger<PushDeliverySweepService>>());

        var result = await svc.SweepOnceAsync(CancellationToken.None);

        result.Claimed.Should().Be(0);
        result.Expired.Should().Be(0);
        store.Updates.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SweepOnce_MixOfExpiredAndNonExpired_OnlyExpiresTheExpired()
    {
        var store = new InMemoryPushStore();
        var audit = new CapturingAudit();
        var svc = new PushDeliverySweepService(store, audit, Mock.Of<ILogger<PushDeliverySweepService>>());

        var now = DateTime.UtcNow;

        // Expired
        var expired1 = NewDelivery(now.AddMinutes(-10), PushDeliveryStatus.Sweeping);
        // Not yet expired
        var notExpired1 = NewDelivery(now.AddMinutes(30), PushDeliveryStatus.Sweeping);

        store.ClaimedBatch.Add(expired1);
        store.ClaimedBatch.Add(notExpired1);
        store.All.Add(expired1);
        store.All.Add(notExpired1);

        var result = await svc.SweepOnceAsync(CancellationToken.None);

        result.Claimed.Should().Be(2);
        result.Expired.Should().Be(1);
        store.Updates.Should().HaveCount(1);
        store.Updates[0].Should().Match<(Guid Id, PushDeliveryStatus Status, string? Error, DateTime? Next, DateTime? Sent)>(
            u => u.Id == expired1.Id && u.Status == PushDeliveryStatus.Expired);
        audit.Entries.Should().HaveCount(1);
        audit.Entries[0].Action.Should().Be("push.expired");
    }

    [Fact]
    public async Task SweepOnce_AllExpired_ExpiresAll()
    {
        var store = new InMemoryPushStore();
        var audit = new CapturingAudit();
        var svc = new PushDeliverySweepService(store, audit, Mock.Of<ILogger<PushDeliverySweepService>>());

        var now = DateTime.UtcNow;
        var d1 = NewDelivery(now.AddMinutes(-30), PushDeliveryStatus.Sweeping);
        var d2 = NewDelivery(now.AddMinutes(-5), PushDeliveryStatus.Sweeping);
        var d3 = NewDelivery(now.AddMinutes(-1), PushDeliveryStatus.Sweeping);

        store.ClaimedBatch.AddRange(new[] { d1, d2, d3 });
        store.All.AddRange(new[] { d1, d2, d3 });

        var result = await svc.SweepOnceAsync(CancellationToken.None);

        result.Expired.Should().Be(3);
        store.Updates.Should().HaveCount(3);
        store.Updates.Should().OnlyContain(u => u.Status == PushDeliveryStatus.Expired);
        audit.Entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task SweepOnce_NonSweepingStatus_Skips()
    {
        var store = new InMemoryPushStore();
        var audit = new CapturingAudit();
        var svc = new PushDeliverySweepService(store, audit, Mock.Of<ILogger<PushDeliverySweepService>>());

        var now = DateTime.UtcNow;
        // Already Sent — should not be in claimed batch but if it is, skip
        var sent = NewDelivery(now.AddMinutes(-5), PushDeliveryStatus.Sent);
        var pending = NewDelivery(now.AddMinutes(-5), PushDeliveryStatus.Pending);

        store.ClaimedBatch.Add(sent);
        store.ClaimedBatch.Add(pending);
        store.All.Add(sent);
        store.All.Add(pending);

        var result = await svc.SweepOnceAsync(CancellationToken.None);

        result.Expired.Should().Be(0);
        store.Updates.Should().BeEmpty();
        audit.Entries.Should().BeEmpty();
    }
}
