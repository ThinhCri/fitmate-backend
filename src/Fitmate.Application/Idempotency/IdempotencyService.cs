using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Idempotency;

/// <summary>
/// Service điều phối idempotency theo DEC-011 + api-contract.md §11.
/// </summary>
/// <remarks>
/// Luồng:
/// <list type="number">
/// <item>Client gửi header <c>Idempotency-Key</c> + payload.</item>
/// <item>Service hash payload + tra store:
/// <list type="bullet">
/// <item>Key chưa có → trả <see cref="IdempotencyDecision.Proceed"/>.</item>
/// <item>Key có + hash khớp + còn hạn → trả <see cref="IdempotencyDecision.Replay"/> + response cũ.</item>
/// <item>Key có + hash khác → trả <see cref="IdempotencyDecision.Conflict"/>.</item>
/// <item>Key có + hết hạn → coi như key mới.</item>
/// </list>
/// </item>
/// <item>Sau khi Controller xử lý xong, gọi <see cref="StoreAsync"/> để ghi response snapshot.</item>
/// </list>
/// </remarks>
public interface IIdempotencyService
{
    Task<IdempotencyDecision> CheckAsync(string key, string payloadHash, CancellationToken ct);

    Task StoreAsync(string key, string payloadHash, int statusCode, string responseBody, CancellationToken ct);
}

/// <summary>
/// Kết quả check idempotency.
/// </summary>
/// <param name="Kind"><see cref="IdempotencyDecisionKind"/>.</param>
/// <param name="StatusCode">HTTP status code lưu lại (cho Replay).</param>
/// <param name="ResponseBody">JSON response lưu lại (cho Replay).</param>
public sealed record IdempotencyDecision(
    IdempotencyDecisionKind Kind,
    int StatusCode = 0,
    string? ResponseBody = null)
{
    public static IdempotencyDecision Proceed() => new(IdempotencyDecisionKind.Proceed);
    public static IdempotencyDecision Replay(int statusCode, string responseBody)
        => new(IdempotencyDecisionKind.Replay, statusCode, responseBody);
    public static IdempotencyDecision Conflict() => new(IdempotencyDecisionKind.Conflict);
}

public enum IdempotencyDecisionKind
{
    Proceed,
    Replay,
    Conflict,
}
