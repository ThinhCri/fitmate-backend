namespace Fitmate.Domain.Outbox;

public class IdempotencyRecord
{
    public const string StatusProcessing = "processing";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";

    public string Key { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public int StatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public string Status { get; private set; } = StatusCompleted;

    private IdempotencyRecord() { }

    public static IdempotencyRecord Record(string key, string payloadHash, int statusCode, string? responseBody, TimeSpan ttl) => new()
    {
        Key = key,
        PayloadHash = payloadHash,
        StatusCode = statusCode,
        ResponseBody = responseBody,
        Status = "completed",
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(ttl)
    };

    public static IdempotencyRecord StartProcessing(string key, string payloadHash, TimeSpan ttl) => new()
    {
        Key = key,
        PayloadHash = payloadHash,
        StatusCode = 0,
        ResponseBody = null,
        Status = "processing",
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(ttl)
    };

    public void Complete(int statusCode, string? responseBody)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Status = "completed";
    }

    public void Fail()
    {
        Status = "failed";
    }
}
