using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

public class FriendRequest : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid FromUserId { get; private set; }
    public Guid ToUserId { get; private set; }
    public FriendRequestStatus Status { get; private set; }

    private FriendRequest() { }
    public static FriendRequest Create(Guid from, Guid to) => new()
    {
        Id = Guid.NewGuid(),
        FromUserId = from,
        ToUserId = to,
        Status = FriendRequestStatus.Pending,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Accept()
    {
        if (Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Request already processed.");
        Status = FriendRequestStatus.Accepted;
        Touch();
    }

    public void Reject()
    {
        if (Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Booking.CannotTransition, "Request already processed.");
        Status = FriendRequestStatus.Rejected;
        Touch();
    }
}
