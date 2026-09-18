using Fitmate.Application.Idempotency;
using Fitmate.Domain.Outbox;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-070: IdempotencyService + in-memory store — 3 nhánh Proceed / Replay / Conflict.
/// </summary>
public class IdempotencyServiceTests
{
    private sealed class InMemoryStore : IIdempotencyStore
    {
        public Dictionary<string, IdempotencyRecord> Items { get; } = new();
        public int AddCalls { get; private set; }

        public Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct)
        {
            if (!Items.TryGetValue(key, out var rec))
                return Task.FromResult(new IdempotencyLookup(null, false));
            var conflict = !string.Equals(rec.PayloadHash, payloadHash, StringComparison.Ordinal);
            return Task.FromResult(new IdempotencyLookup(rec, conflict));
        }

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct)
        {
            AddCalls++;
            Items[record.Key] = record;
            return Task.CompletedTask;
        }

        public Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct)
        {
            var keys = Items.Where(kv => kv.Value.ExpiresAt <= cutoffUtc).Select(kv => kv.Key).ToList();
            foreach (var k in keys) Items.Remove(k);
            return Task.FromResult(keys.Count);
        }
    }

    private static IdempotencyService NewService(InMemoryStore store) =>
        new(store, ttl: TimeSpan.FromHours(24));

    [Fact]
    public async Task Check_NewKey_ReturnsProceed()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var decision = await svc.CheckAsync("key-abc-123", "hash-A", CancellationToken.None);
        decision.Kind.Should().Be(IdempotencyDecisionKind.Proceed);
    }

    [Fact]
    public async Task Check_SameKeySameHash_ReturnsReplay()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        await svc.StoreAsync("key-abc-123", "hash-A", 201, "{\"id\":\"x\"}", CancellationToken.None);

        var decision = await svc.CheckAsync("key-abc-123", "hash-A", CancellationToken.None);
        decision.Kind.Should().Be(IdempotencyDecisionKind.Replay);
        decision.StatusCode.Should().Be(201);
        decision.ResponseBody.Should().Contain("\"id\":\"x\"");
    }

    [Fact]
    public async Task Check_SameKeyDifferentHash_ReturnsConflict()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        await svc.StoreAsync("key-abc-123", "hash-A", 201, "{\"id\":\"x\"}", CancellationToken.None);

        var decision = await svc.CheckAsync("key-abc-123", "hash-B", CancellationToken.None);
        decision.Kind.Should().Be(IdempotencyDecisionKind.Conflict);
    }

    [Fact]
    public async Task Check_ExpiredRecord_ProceedsAgain()
    {
        var store = new InMemoryStore();
        var svc = new IdempotencyService(store, ttl: TimeSpan.FromMilliseconds(1));
        await svc.StoreAsync("key-abc-123", "hash-A", 201, "{}", CancellationToken.None);
        await Task.Delay(20);

        var decision = await svc.CheckAsync("key-abc-123", "hash-A", CancellationToken.None);
        decision.Kind.Should().Be(IdempotencyDecisionKind.Proceed);
    }

    [Fact]
    public async Task Store_WritesRecordWithExpiry()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        await svc.StoreAsync("key-abc-123", "hash-A", 200, "ok", CancellationToken.None);
        store.AddCalls.Should().Be(1);
        var rec = store.Items["key-abc-123"];
        rec.ExpiresAt.Should().BeAfter(rec.CreatedAt);
        (rec.ExpiresAt - rec.CreatedAt).TotalHours.Should().BeApproximately(24, 0.01);
    }
}
