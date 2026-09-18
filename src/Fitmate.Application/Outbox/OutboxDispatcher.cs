using System.Text.Json;
using Fitmate.Domain.Outbox;

namespace Fitmate.Application.Outbox;

/// <summary>
/// Dispatcher enqueue message vào outbox ngay trong transaction của use case.
/// Không gọi external service ở đây — worker sẽ dispatch sau (DEC-020).
/// </summary>
/// <remarks>
/// <para>Dispatcher không phụ thuộc Infrastructure: Application chỉ biết
/// interface <see cref="IOutboxQueue"/>; Infrastructure cung cấp EF Core-backed
/// implementation đăng ký vào DI.</para>
/// <para>Use case gọi <c>dispatcher.Enqueue(...)</c> → SaveChangesAsync (commit message + business row
/// cùng transaction).</para>
/// </remarks>
public interface IOutboxDispatcher
{
    /// <summary>
    /// Enqueue message vào outbox. Chưa SaveChanges — caller quyết định khi nào commit.
    /// </summary>
    void Enqueue(string type, object payload);
}

/// <summary>
/// Hợp đồng tối thiểu Application cần từ DbContext: thêm 1 OutboxMessage.
/// Infrastructure cung cấp implementation chuyển tiếp sang <c>FitmateDbContext.OutboxMessages</c>.
/// </summary>
public interface IOutboxQueue
{
    void Add(OutboxMessage message);
}

/// <summary>
/// Pure helper serialize payload → JSON để đảm báo format thống nhất (camelCase, no indent).
/// </summary>
public static class OutboxPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}

public class OutboxDispatcher : IOutboxDispatcher
{
    private readonly IOutboxQueue _queue;

    public OutboxDispatcher(IOutboxQueue queue)
    {
        _queue = queue;
    }

    public void Enqueue(string type, object payload)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Outbox type must not be empty", nameof(type));
        ArgumentNullException.ThrowIfNull(payload);

        var json = OutboxPayload.Serialize(payload);
        _queue.Add(OutboxMessage.Enqueue(type, json));
    }
}
