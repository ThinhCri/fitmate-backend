using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Idempotency;

/// <summary>
/// Kết quả tra cứu idempotency record theo key.
/// </summary>
/// <param name="Record">Record đã lưu (null nếu key chưa từng dùng).</param>
/// <param name="Conflict">True nếu key tồn tại nhưng payload hash khác (client gửi lại key với body khác).</param>
public sealed record IdempotencyLookup(IdempotencyRecord? Record, bool Conflict);

/// <summary>
/// Lưu trữ idempotency record. DB chỉ chứa hash + response snapshot;
/// raw key gửi từ client không bao giờ lưu DB.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyLookup> FindAsync(string key, string payloadHash, CancellationToken ct);

    Task AddAsync(IdempotencyRecord record, CancellationToken ct);

    Task<int> PurgeExpiredAsync(DateTime cutoffUtc, CancellationToken ct);
}
