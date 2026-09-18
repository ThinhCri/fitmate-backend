using Fitmate.Domain.Common;

namespace Fitmate.Domain.Notifications;

public class Device : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    /// <summary>
    /// Hashed token (PBKDF2/SHA256) — không lưu raw token.
    /// </summary>
    public string TokenHash { get; private set; } = null!;
    public DevicePlatform Platform { get; private set; }

    private Device() { }
    public static Device Create(Guid userId, string tokenHash, DevicePlatform platform) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        Platform = platform,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
