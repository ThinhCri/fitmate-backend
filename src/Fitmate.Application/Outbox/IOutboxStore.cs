using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Outbox;

/// <summary>
/// Store cho Outbox pattern (DEC-012). Worker đọc batch, đánh dấu done/failed qua store.
/// </summary>
public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxMessage>> FetchDueAsync(int batchSize, DateTime nowUtc, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);

    Task<int> PurgeDoneOlderThanAsync(DateTime cutoffUtc, CancellationToken ct);
}

/// <summary>
/// Handler xử lý 1 loại message. <see cref="Type"/> phải khớp <c>OutboxMessage.Type</c>.
/// </summary>
public interface IOutboxHandler
{
    /// <summary>Tên type, vd <c>"booking.confirmed"</c>.</summary>
    string Type { get; }

    /// <summary>Xử lý payload JSON. Throw nếu lỗi → worker sẽ retry với backoff.</summary>
    Task HandleAsync(string payloadJson, CancellationToken ct);
}
