using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Idempotency;

/// <summary>
/// Service điều phối idempotency theo DEC-011 + api-contract.md §11 + Plan v9.1.
/// </summary>
public interface IIdempotencyService
{
    Task<IdempotencyAcquireResult> AcquireAsync(
        string key,
        string payloadHash,
        Guid ownerToken,
        bool isFinancial,
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

    Task<IdempotencyDecision> CheckAsync(string key, string payloadHash, CancellationToken ct);

    Task StoreAsync(string key, string payloadHash, int statusCode, string responseBody, CancellationToken ct);
}

/// <summary>
/// Kết quả check idempotency (legacy compatibility).
/// </summary>
public sealed record IdempotencyDecision(
    IdempotencyDecisionKind Kind,
    int StatusCode = 0,
    string? ResponseBody = null)
{
    public static IdempotencyDecision Proceed() => new(IdempotencyDecisionKind.Proceed);
    public static IdempotencyDecision Replay(int statusCode, string responseBody)
        => new(IdempotencyDecisionKind.Replay, statusCode, responseBody);
    public static IdempotencyDecision Conflict() => new(IdempotencyDecisionKind.Conflict);
    public static IdempotencyDecision InProgress() => new(IdempotencyDecisionKind.InProgress);
}

public enum IdempotencyDecisionKind
{
    Proceed,
    Replay,
    Conflict,
    InProgress,
}
