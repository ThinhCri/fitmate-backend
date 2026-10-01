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
    public DateTime? ExpiresAt { get; private set; }
    public string Status { get; private set; } = StatusCompleted;
    public Guid OwnerToken { get; private set; }
    public DateTime LeaseExpiresAt { get; private set; }
    public bool IsFinancial { get; private set; }
    public string? ActorUserId { get; private set; }
    public string? OperationName { get; private set; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord CreateAcquired(
        string key,
        string payloadHash,
        Guid ownerToken,
        TimeSpan leaseDuration,
        bool isFinancial,
        TimeSpan? ttl,
        string? actorUserId = null,
        string? operationName = null)
    {
        var now = DateTime.UtcNow;
        return new IdempotencyRecord
        {
            Key = key,
            PayloadHash = payloadHash,
            OwnerToken = ownerToken,
            StatusCode = 0,
            ResponseBody = null,
            Status = StatusProcessing,
            CreatedAt = now,
            LeaseExpiresAt = now.Add(leaseDuration),
            IsFinancial = isFinancial,
            ExpiresAt = isFinancial || ttl == null ? null : now.Add(ttl.Value),
            ActorUserId = actorUserId,
            OperationName = operationName
        };
    }

    public static IdempotencyRecord Record(string key, string payloadHash, int statusCode, string? responseBody, TimeSpan ttl) => new()
    {
        Key = key,
        PayloadHash = payloadHash,
        StatusCode = statusCode,
        ResponseBody = responseBody,
        Status = StatusCompleted,
        OwnerToken = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(ttl),
        LeaseExpiresAt = DateTime.UtcNow,
        IsFinancial = false
    };

    public static IdempotencyRecord CompletedRecord(
        string key,
        string payloadHash,
        int statusCode,
        string? responseBody,
        bool isFinancial,
        TimeSpan? ttl,
        string? actorUserId = null,
        string? operationName = null)
    {
        var now = DateTime.UtcNow;
        return new IdempotencyRecord
        {
            Key = key,
            PayloadHash = payloadHash,
            OwnerToken = Guid.NewGuid(),
            StatusCode = statusCode,
            ResponseBody = responseBody,
            Status = StatusCompleted,
            CreatedAt = now,
            LeaseExpiresAt = now,
            IsFinancial = isFinancial,
            ExpiresAt = isFinancial || ttl == null ? null : now.Add(ttl.Value),
            ActorUserId = actorUserId,
            OperationName = operationName
        };
    }

    public static IdempotencyRecord StartProcessing(string key, string payloadHash, TimeSpan ttl) => new()
    {
        Key = key,
        PayloadHash = payloadHash,
        StatusCode = 0,
        ResponseBody = null,
        Status = StatusProcessing,
        OwnerToken = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.Add(ttl),
        LeaseExpiresAt = DateTime.UtcNow.Add(ttl),
        IsFinancial = false
    };

    public void Complete(int statusCode, string? responseBody, Guid? ownerToken = null)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Status = StatusCompleted;
        if (ownerToken.HasValue)
        {
            OwnerToken = ownerToken.Value;
        }
    }

    public void Fail(Guid? ownerToken = null)
    {
        Status = StatusFailed;
        if (ownerToken.HasValue)
        {
            OwnerToken = ownerToken.Value;
        }
    }
}
