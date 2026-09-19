using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

/// <summary>
/// BE-181: Block do user tự thiết lập, tách khỏi report/moderation.
/// Block 1 chiều (BlockerId, BlockedId). Hệ thống enforce block theo cả 2 chiều
/// ở Application layer; cấm tự block.
/// </summary>
public class BlockedUser : AuditableEntity
{
    public Guid BlockerId { get; private set; }
    public Guid BlockedId { get; private set; }

    private BlockedUser() { }

    public static BlockedUser Create(Guid blockerId, Guid blockedId)
    {
        if (blockerId == blockedId) throw new ArgumentException("Cannot block self.", nameof(blockerId));
        return new BlockedUser
        {
            BlockerId = blockerId,
            BlockedId = blockedId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
