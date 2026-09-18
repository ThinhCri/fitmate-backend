using Fitmate.Application.Outbox;
using Fitmate.Domain.Outbox;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-070: OutboxWorker + dispatcher + handler. In-memory store + handler giả.
/// </summary>
public class OutboxWorkerTests
{
    private sealed class InMemoryOutboxStore : IOutboxStore
    {
        public List<OutboxMessage> Messages { get; } = new();
        public int SaveCalls { get; private set; }
        public int PurgeCalls { get; private set; }

        public Task<IReadOnlyList<OutboxMessage>> FetchDueAsync(int batchSize, DateTime nowUtc, CancellationToken ct)
        {
            IReadOnlyList<OutboxMessage> due = Messages
                .Where(m => m.Status == "pending" && m.NextAttemptAt <= nowUtc)
                .OrderBy(m => m.NextAttemptAt)
                .Take(batchSize)
                .ToList();
            return Task.FromResult(due);
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task<int> PurgeDoneOlderThanAsync(DateTime cutoffUtc, CancellationToken ct)
        {
            PurgeCalls++;
            var n = Messages.RemoveAll(m => m.Status == "done" && m.ProcessedAt != null && m.ProcessedAt <= cutoffUtc);
            return Task.FromResult(n);
        }
    }

    private sealed class FakeHandler : IOutboxHandler
    {
        public string Type { get; }
        public int Calls { get; private set; }
        public Func<string, Task>? OnHandle { get; set; }

        public FakeHandler(string type, Func<string, Task>? onHandle = null)
        {
            Type = type;
            OnHandle = onHandle;
        }

        public Task HandleAsync(string payloadJson, CancellationToken ct)
        {
            Calls++;
            if (OnHandle is null) return Task.CompletedTask;
            return OnHandle(payloadJson);
        }
    }

    private sealed class CapturingQueue : IOutboxQueue
    {
        private readonly InMemoryOutboxStore _store;
        public CapturingQueue(InMemoryOutboxStore store) { _store = store; }
        public void Add(OutboxMessage message) => _store.Messages.Add(message);
    }

    [Fact]
    public async Task Dispatcher_Enqueue_AddsMessageToQueue()
    {
        var store = new InMemoryOutboxStore();
        var queue = new CapturingQueue(store);
        var dispatcher = new OutboxDispatcher(queue);

        dispatcher.Enqueue("booking.confirmed", new { bookingId = "abc", memberId = "u-1" });

        store.Messages.Should().HaveCount(1);
        var msg = store.Messages[0];
        msg.Type.Should().Be("booking.confirmed");
        msg.Status.Should().Be("pending");
        msg.PayloadJson.Should().Contain("\"bookingId\":\"abc\"");
    }

    [Fact]
    public async Task Dispatcher_Enqueue_EmptyType_Throws()
    {
        var queue = new CapturingQueue(new InMemoryOutboxStore());
        var dispatcher = new OutboxDispatcher(queue);
        var act = () => dispatcher.Enqueue("", new { });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Worker_ProcessesPendingMessages_MarksDone()
    {
        var store = new InMemoryOutboxStore();
        var handler = new FakeHandler("booking.confirmed");
        var worker = new OutboxWorker(
            store,
            new IOutboxHandler[] { handler },
            NullLogger<OutboxWorker>.Instance);

        var msg = OutboxMessage.Enqueue("booking.confirmed", "{}");
        store.Messages.Add(msg);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().Be(1);
        handler.Calls.Should().Be(1);
        msg.Status.Should().Be("done");
        msg.ProcessedAt.Should().NotBeNull();
        store.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Worker_HandlerThrows_IncrementsAttemptsAndBackoff()
    {
        var store = new InMemoryOutboxStore();
        var handler = new FakeHandler("booking.failed", _ => throw new InvalidOperationException("boom"));
        var worker = new OutboxWorker(
            store,
            new IOutboxHandler[] { handler },
            NullLogger<OutboxWorker>.Instance);

        var msg = OutboxMessage.Enqueue("booking.failed", "{}");
        store.Messages.Add(msg);

        await worker.ProcessOnceAsync(CancellationToken.None);

        msg.Status.Should().Be("pending");
        msg.Attempts.Should().Be(1);
        msg.LastError.Should().Contain("boom");
        msg.NextAttemptAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Worker_NoHandlerForType_LeavesPendingWithBackoff()
    {
        var store = new InMemoryOutboxStore();
        var worker = new OutboxWorker(
            store,
            Array.Empty<IOutboxHandler>(),
            NullLogger<OutboxWorker>.Instance);

        var msg = OutboxMessage.Enqueue("unknown.type", "{}");
        store.Messages.Add(msg);

        await worker.ProcessOnceAsync(CancellationToken.None);

        msg.Status.Should().Be("pending");
        msg.Attempts.Should().Be(1);
        msg.LastError.Should().Be("NO_HANDLER");
    }

    [Fact]
    public async Task Worker_DoesNotProcessFutureMessages()
    {
        var store = new InMemoryOutboxStore();
        var handler = new FakeHandler("x.confirmed");
        var worker = new OutboxWorker(
            store,
            new IOutboxHandler[] { handler },
            NullLogger<OutboxWorker>.Instance);

        var msg = OutboxMessage.Enqueue("x.confirmed", "{}");
        msg.MarkFailed("test", TimeSpan.FromHours(1));
        store.Messages.Add(msg);

        var processed = await worker.ProcessOnceAsync(CancellationToken.None);

        processed.Should().Be(0);
        handler.Calls.Should().Be(0);
    }
}
