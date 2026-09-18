using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Idempotency;

/// <summary>
/// Default <see cref="IIdempotencyService"/> impl, dùng <see cref="IIdempotencyStore"/> + TTL config.
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IIdempotencyStore _store;
    private readonly TimeSpan _ttl;

    public IdempotencyService(IIdempotencyStore store, TimeSpan? ttl = null)
    {
        _store = store;
        _ttl = ttl ?? TimeSpan.FromHours(24); // DEC-011 / api-contract.md §11
    }

    public async Task<IdempotencyDecision> CheckAsync(string key, string payloadHash, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must not be empty", nameof(key));
        if (string.IsNullOrWhiteSpace(payloadHash))
            throw new ArgumentException("Payload hash must not be empty", nameof(payloadHash));

        var lookup = await _store.FindAsync(key, payloadHash, ct);
        if (lookup.Record is null)
        {
            // Key chưa từng dùng, hoặc đã purge → Proceed.
            return IdempotencyDecision.Proceed();
        }

        // Record còn hạn?
        if (lookup.Record.ExpiresAt <= DateTime.UtcNow)
        {
            return IdempotencyDecision.Proceed();
        }

        if (lookup.Conflict)
        {
            return IdempotencyDecision.Conflict();
        }

        return IdempotencyDecision.Replay(lookup.Record.StatusCode, lookup.Record.ResponseBody ?? string.Empty);
    }

    public Task StoreAsync(string key, string payloadHash, int statusCode, string responseBody, CancellationToken ct)
    {
        var record = IdempotencyRecord.Record(key, payloadHash, statusCode, responseBody, _ttl);
        return _store.AddAsync(record, ct);
    }
}
