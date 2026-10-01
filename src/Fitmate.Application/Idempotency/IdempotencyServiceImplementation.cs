using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Idempotency;

/// <summary>
/// Default <see cref="IIdempotencyService"/> impl, dùng <see cref="IIdempotencyStore"/> + TTL & Lease config.
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IIdempotencyStore _store;
    private readonly TimeSpan _ttl;
    private readonly TimeSpan _leaseDuration;

    public IdempotencyService(IIdempotencyStore store, TimeSpan? ttl = null, TimeSpan? leaseDuration = null)
    {
        _store = store;
        _ttl = ttl ?? TimeSpan.FromHours(24); // DEC-011 / api-contract.md §11
        _leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(60);
    }

    public async Task<IdempotencyAcquireResult> AcquireAsync(
        string key,
        string payloadHash,
        Guid ownerToken,
        bool isFinancial,
        string? actorUserId,
        string? operationName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must not be empty", nameof(key));
        if (string.IsNullOrWhiteSpace(payloadHash))
            throw new ArgumentException("Payload hash must not be empty", nameof(payloadHash));

        return await _store.TryAcquireAsync(
            key,
            payloadHash,
            ownerToken,
            _leaseDuration,
            isFinancial,
            _ttl,
            actorUserId,
            operationName,
            ct);
    }

    public Task<bool> CompleteAsync(
        string key,
        Guid ownerToken,
        int statusCode,
        string? responseBody,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must not be empty", nameof(key));

        return _store.CompleteAsync(key, ownerToken, statusCode, responseBody, ct);
    }

    public Task<bool> FailAsync(
        string key,
        Guid ownerToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must not be empty", nameof(key));

        return _store.FailAsync(key, ownerToken, ct);
    }

    public Task<bool> ReleaseAsync(
        string key,
        Guid ownerToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key must not be empty", nameof(key));

        return _store.ReleaseAsync(key, ownerToken, ct);
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
            return IdempotencyDecision.Proceed();
        }

        if (lookup.Record.ExpiresAt.HasValue && lookup.Record.ExpiresAt.Value <= DateTime.UtcNow)
        {
            return IdempotencyDecision.Proceed();
        }

        if (lookup.Conflict)
        {
            return IdempotencyDecision.Conflict();
        }

        if (lookup.IsProcessing)
        {
            return IdempotencyDecision.InProgress();
        }

        return IdempotencyDecision.Replay(lookup.Record.StatusCode, lookup.Record.ResponseBody ?? string.Empty);
    }

    public Task StoreAsync(string key, string payloadHash, int statusCode, string responseBody, CancellationToken ct)
    {
        var record = IdempotencyRecord.Record(key, payloadHash, statusCode, responseBody, _ttl);
        return _store.AddAsync(record, ct);
    }
}
