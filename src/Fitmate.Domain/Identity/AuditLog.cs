using Fitmate.Domain.Common;

namespace Fitmate.Domain.Identity;

/// <summary>
/// Audit log cho hành động nhạy cảm (admin approve cert, refund, lock user, role change...).
/// </summary>
public class AuditLog
{
    public Guid Id { get; private set; }
    public Guid? ActorId { get; private set; }
    public string ActorRole { get; private set; } = "system";
    public string Action { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public Guid? TargetId { get; private set; }
    public string? Reason { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog Record(
        Guid? actorId,
        string actorRole,
        string action,
        string targetType,
        Guid? targetId,
        string? reason = null,
        string? metadataJson = null) => new()
        {
            Id = Guid.NewGuid(),
            ActorId = actorId,
            ActorRole = actorRole,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Reason = reason,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.UtcNow
        };
}
