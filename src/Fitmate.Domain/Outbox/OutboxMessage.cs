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
    public string Status { get; private set; } = "pending"; // pending | done | failed | dead_letter
    public string? LastError { get; private set; }

    public const int MaxAttempts = 5;

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
        Attempts++;
        LastError = error;
        if (Attempts >= MaxAttempts)
        {
            Status = "dead_letter";
            NextAttemptAt = DateTime.MaxValue.ToUniversalTime();
        }
        else
        {
            Status = "pending";
            NextAttemptAt = DateTime.UtcNow.Add(backoff);
        }
    }

    public void MarkDeadLetter(string error)
    {
        Status = "dead_letter";
        LastError = error;
        NextAttemptAt = DateTime.MaxValue.ToUniversalTime();
    }

    public void Replay()
    {
        if (Status != "dead_letter")
        {
            throw new InvalidOperationException($"Cannot replay outbox message in status '{Status}'. Only 'dead_letter' messages can be replayed.");
        }

        Status = "pending";
        Attempts = 0;
        NextAttemptAt = DateTime.UtcNow;
        // LastError is intentionally preserved for audit and diagnostics
    }
}
