using Fitmate.Application.Idempotency;
using Fitmate.Domain.Outbox;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-070 + BE-810: IdempotencyService + atomic acquisition protocol tests.
/// </summary>
public class IdempotencyServiceTests
{
    private sealed class InMemoryStore : IIdempotencyStore
    {
        public Dictionary<string, IdempotencyRecord> Items { get; } = new();
        public int AddCalls { get; private set; }

        public Task<IdempotencyAcquireResult> TryAcquireAsync(
            string key,
            string payloadHash,
            Guid ownerToken,
            TimeSpan leaseDuration,
            bool isFinancial,
            TimeSpan? ttl,
            string? actorUserId,
            string? operationName,
            CancellationToken ct)
        {
            if (!Items.TryGetValue(key, out var rec))
            {
                var newRec = IdempotencyRecord.CreateAcquired(key, payloadHash, ownerToken, leaseDuration, isFinancial, ttl, actorUserId, operationName);
                Items[key] = newRec;
                return Task.FromResult(IdempotencyAcquireResult.Acquired(newRec));
            }

            if (!string.Equals(rec.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return Task.FromResult(IdempotencyAcquireResult.PayloadConflict());
            }

            if (rec.Status == IdempotencyRecord.StatusCompleted)
            {
                return Task.FromResult(IdempotencyAcquireResult.Replay(rec));
            }

            if (rec.Status == IdempotencyRecord.StatusProcessing && rec.LeaseExpiresAt > DateTime.UtcNow)
            {
                return Task.FromResult(IdempotencyAcquireResult.InProgress(rec));
            }

            // Stale or failed with same hash -> takeover
            var takeover = IdempotencyRecord.CreateAcquired(key, payloadHash, ownerToken, leaseDuration, isFinancial, ttl, actorUserId, operationName);
            Items[key] = takeover;
            return Task.FromResult(IdempotencyAcquireResult.Acquired(takeover));
        }

        public Task<bool> CompleteAsync(string key, Guid ownerToken, int statusCode, string? responseBody, CancellationToken ct)
        {
            if (Items.TryGetValue(key, out var rec) && rec.OwnerToken == ownerToken)
            {
                rec.Complete(statusCode, responseBody, ownerToken);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> FailAsync(string key, Guid ownerToken, CancellationToken ct)
        {
            if (Items.TryGetValue(key, out var rec) && rec.OwnerToken == ownerToken)
            {
                rec.Fail(ownerToken);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ReleaseAsync(string key, Guid ownerToken, CancellationToken ct)
        {
            if (Items.TryGetValue(key, out var rec) && rec.OwnerToken == ownerToken)
            {
                Items.Remove(key);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct)
        {
            if (!Items.TryGetValue(key, out var rec))
                return Task.FromResult(new IdempotencyLookup(null, false, false));
            var conflict = !string.Equals(rec.PayloadHash, payloadHash, StringComparison.Ordinal);
            var isProcessing = rec.Status == IdempotencyRecord.StatusProcessing;
            return Task.FromResult(new IdempotencyLookup(rec, conflict, isProcessing));
        }

        public Task AddAsync(IdempotencyRecord record, CancellationToken ct)
        {
            AddCalls++;
            Items[record.Key] = record;
            return Task.CompletedTask;
        }

        public Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct)
        {
            var keys = Items.Where(kv => !kv.Value.IsFinancial && kv.Value.ExpiresAt != null && kv.Value.ExpiresAt <= cutoffUtc).Select(kv => kv.Key).ToList();
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
    public async Task Check_RecordProcessing_ReturnsInProgress()
    {
        var store = new InMemoryStore();
        var rec = IdempotencyRecord.StartProcessing("key-in-prog", "hash-A", TimeSpan.FromMinutes(10));
        store.Items["key-in-prog"] = rec;
        var svc = NewService(store);

        var decision = await svc.CheckAsync("key-in-prog", "hash-A", CancellationToken.None);
        decision.Kind.Should().Be(IdempotencyDecisionKind.InProgress);
    }

    [Fact]
    public async Task AcquireAsync_FreshKey_ReturnsAcquired()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var owner = Guid.NewGuid();

        var result = await svc.AcquireAsync("key-acquire-1", "hash-A", owner, isFinancial: true, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);
        result.Status.Should().Be(IdempotencyAcquireStatus.Acquired);
        result.Record.Should().NotBeNull();
        result.Record!.OwnerToken.Should().Be(owner);
    }

    [Fact]
    public async Task AcquireAsync_SecondCallBeforeCompletion_ReturnsInProgress()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var owner1 = Guid.NewGuid();
        var owner2 = Guid.NewGuid();

        var r1 = await svc.AcquireAsync("key-acquire-2", "hash-A", owner1, isFinancial: false, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);
        r1.Status.Should().Be(IdempotencyAcquireStatus.Acquired);

        var r2 = await svc.AcquireAsync("key-acquire-2", "hash-A", owner2, isFinancial: false, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);
        r2.Status.Should().Be(IdempotencyAcquireStatus.InProgress);
    }

    [Fact]
    public async Task AcquireAsync_DifferentPayload_ReturnsPayloadConflict()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var owner = Guid.NewGuid();

        await svc.AcquireAsync("key-acquire-3", "hash-A", owner, isFinancial: false, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);

        var rConflict = await svc.AcquireAsync("key-acquire-3", "hash-B", Guid.NewGuid(), isFinancial: false, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);
        rConflict.Status.Should().Be(IdempotencyAcquireStatus.PayloadConflict);
    }

    [Fact]
    public async Task CompleteAsync_WithWrongOwnerToken_Fails()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var owner = Guid.NewGuid();

        await svc.AcquireAsync("key-acquire-4", "hash-A", owner, isFinancial: true, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);

        var wrongToken = Guid.NewGuid();
        var completed = await svc.CompleteAsync("key-acquire-4", wrongToken, 200, "{}", CancellationToken.None);
        completed.Should().BeFalse();

        var correctCompleted = await svc.CompleteAsync("key-acquire-4", owner, 200, "{}", CancellationToken.None);
        correctCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Purge_DoesNotDeleteFinancialRecords()
    {
        var store = new InMemoryStore();
        var svc = NewService(store);
        var ownerFin = Guid.NewGuid();
        var ownerNonFin = Guid.NewGuid();

        await svc.AcquireAsync("fin-1", "hash-A", ownerFin, isFinancial: true, actorUserId: "u1", operationName: "POST /pay", CancellationToken.None);
        await svc.CompleteAsync("fin-1", ownerFin, 200, "{}", CancellationToken.None);

        await svc.AcquireAsync("nonfin-1", "hash-A", ownerNonFin, isFinancial: false, actorUserId: "u1", operationName: "POST /msg", CancellationToken.None);
        await svc.CompleteAsync("nonfin-1", ownerNonFin, 200, "{}", CancellationToken.None);

        // Advance expiration for nonfin
        store.Items["nonfin-1"] = IdempotencyRecord.Record("nonfin-1", "hash-A", 200, "{}", TimeSpan.FromMilliseconds(-100));

        var purged = await store.PurgeExpiredAsync(DateTime.UtcNow, CancellationToken.None);
        purged.Should().Be(1);
        store.Items.ContainsKey("fin-1").Should().BeTrue("Financial records must be preserved forever");
        store.Items.ContainsKey("nonfin-1").Should().BeFalse();
    }
}
