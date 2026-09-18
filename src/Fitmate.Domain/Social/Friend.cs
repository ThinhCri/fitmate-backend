using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class Friend : AuditableEntity
{
    public Guid UserId { get; private set; }
    public Guid FriendUserId { get; private set; }

    private Friend() { }
    public static Friend Create(Guid userId, Guid friendUserId) => new()
    {
        UserId = userId,
        FriendUserId = friendUserId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
