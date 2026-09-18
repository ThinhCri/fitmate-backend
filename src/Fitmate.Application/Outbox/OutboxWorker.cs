using Fitmate.Domain.Outbox;
using Microsoft.Extensions.Logging;

namespace Fitmate.Application.Outbox;

/// <summary>
/// Worker poll outbox theo chu kỳ, dispatch message tới handler đăng ký theo Type.
/// Retry với exponential backoff (DEC-012).
/// </summary>
/// <remarks>
/// Worker KHÔNG giữ transaction DB mở khi gọi handler — đúng DEC-020 (không giữ transaction khi gọi provider).
/// Mỗi message:
/// <list type="number">
/// <item>Fetch batch <c>pending</c> + <c>NextAttemptAt &lt;= now</c>.</item>
/// <item>Với mỗi message: resolve handler theo <c>Type</c>.</item>
/// <item>Handler throw → MarkFailed với backoff (1s, 5s, 30s, 5m, capped tại 1 giờ).</item>
/// <item>Handler pass → MarkDone.</item>
/// </list>
/// </remarks>
public class OutboxWorker
{
    private readonly IOutboxStore _store;
    private readonly IReadOnlyDictionary<string, IOutboxHandler> _handlers;
    private readonly ILogger<OutboxWorker> _logger;
    private readonly OutboxOptions _options;

    public OutboxWorker(
        IOutboxStore store,
        IEnumerable<IOutboxHandler> handlers,
        ILogger<OutboxWorker> logger,
        OutboxOptions? options = null)
    {
        _store = store;
        _logger = logger;
        _options = options ?? new OutboxOptions();
        _handlers = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);
    }

    /// <summary>
    /// Chạy 1 lượt (1 tick). Tick tiếp theo do caller quyết định (BackgroundService / test).
    /// </summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        var batch = await _store.FetchDueAsync(_options.BatchSize, DateTime.UtcNow, ct);
        if (batch.Count == 0) return 0;

        foreach (var msg in batch)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessOneAsync(msg, ct);
        }

        await _store.SaveChangesAsync(ct);
        return batch.Count;
    }

    private async Task ProcessOneAsync(OutboxMessage msg, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(msg.Type, out var handler))
        {
            _logger.LogError("Outbox: no handler registered for type {Type}, message {Id} will retry", msg.Type, msg.Id);
            msg.MarkFailed("NO_HANDLER", Backoff(msg.Attempts));
            return;
        }

        try
        {
            await handler.HandleAsync(msg.PayloadJson, ct);
            msg.MarkDone();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var backoff = Backoff(msg.Attempts);
            _logger.LogWarning(ex, "Outbox: handler {Type} failed for {Id}, retry in {Backoff}", msg.Type, msg.Id, backoff);
            msg.MarkFailed(Truncate(ex.Message, 1000), backoff);
        }
    }

    private static TimeSpan Backoff(int attempts) => attempts switch
    {
        0 => TimeSpan.FromSeconds(1),
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        3 => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromHours(1),
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    public sealed record OutboxOptions(int BatchSize = 50);
}
