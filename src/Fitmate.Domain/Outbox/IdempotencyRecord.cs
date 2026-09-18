namespace Fitmate.Domain.Outbox;

public class IdempotencyRecord
{
    public string Key { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public int StatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord Record(string key, string payloadHash, int statusCode, string? responseBody, TimeSpan ttl) => new()
    {
        Key = key,
        PayloadHash = payloadHash,
        StatusCode = statusCode,
        ResponseBody = responseBody,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(ttl)
    };
}
