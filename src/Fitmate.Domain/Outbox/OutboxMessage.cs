using Fitmate.Domain.Common;

namespace Fitmate.Domain.Outbox;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public string Status { get; private set; } = "pending"; // pending | done | failed
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Enqueue(string type, string payloadJson) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        PayloadJson = payloadJson,
        CreatedAt = DateTime.UtcNow,
        NextAttemptAt = DateTime.UtcNow
    };

    public void MarkDone()
    {
        Status = "done";
        ProcessedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error, TimeSpan backoff)
    {
        Status = "pending";
        Attempts++;
        LastError = error;
        NextAttemptAt = DateTime.UtcNow.Add(backoff);
    }
}
