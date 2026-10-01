using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Idempotency;

public enum IdempotencyAcquireStatus
{
    Acquired,
    InProgress,
    Replay,
    PayloadConflict
}

public sealed record IdempotencyAcquireResult(
    IdempotencyAcquireStatus Status,
    IdempotencyRecord? Record = null)
{
    public static IdempotencyAcquireResult Acquired(IdempotencyRecord record) => new(IdempotencyAcquireStatus.Acquired, record);
    public static IdempotencyAcquireResult InProgress(IdempotencyRecord record) => new(IdempotencyAcquireStatus.InProgress, record);
    public static IdempotencyAcquireResult Replay(IdempotencyRecord record) => new(IdempotencyAcquireStatus.Replay, record);
    public static IdempotencyAcquireResult PayloadConflict() => new(IdempotencyAcquireStatus.PayloadConflict);
}

/// <summary>
/// Kết quả tra cứu idempotency record theo key.
/// </summary>
public sealed record IdempotencyLookup(IdempotencyRecord? Record, bool Conflict, bool IsProcessing = false);

/// <summary>
/// Lưu trữ idempotency record. DB chỉ chứa hash + response snapshot;
/// raw key gửi từ client không bao giờ lưu DB.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyAcquireResult> TryAcquireAsync(
        string key,
        string payloadHash,
        Guid ownerToken,
        TimeSpan leaseDuration,
        bool isFinancial,
        TimeSpan? ttl,
        string? actorUserId,
        string? operationName,
        CancellationToken ct);

    Task<bool> CompleteAsync(
        string key,
        Guid ownerToken,
        int statusCode,
        string? responseBody,
        CancellationToken ct);

    Task<bool> FailAsync(
        string key,
        Guid ownerToken,
        CancellationToken ct);

    Task<bool> ReleaseAsync(
        string key,
        Guid ownerToken,
        CancellationToken ct);

    Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct);

    Task AddAsync(IdempotencyRecord record, CancellationToken ct);

    Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct);
}
