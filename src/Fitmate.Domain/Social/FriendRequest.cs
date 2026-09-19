using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

/// <summary>
/// BE-181: FriendRequest là bảng riêng, có hướng (SenderId → RecipientId).
/// Không có auto-accept khi đối phương gửi ngược; chỉ 1 Pending request cho mỗi cặp.
/// </summary>
public class FriendRequest : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid SenderId { get; private set; }
    public Guid RecipientId { get; private set; }
    public FriendRequestStatus Status { get; private set; }

    private FriendRequest() { }

    public static FriendRequest Create(Guid senderId, Guid recipientId)
    {
        if (senderId == recipientId)
            throw new ArgumentException("Cannot send friend request to self.", nameof(senderId));
        return new FriendRequest
        {
            Id = Guid.NewGuid(),
            SenderId = senderId,
            RecipientId = recipientId,
            Status = FriendRequestStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Accept()
    {
        if (Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Request already processed.");
        Status = FriendRequestStatus.Accepted;
        Touch();
    }

    public void Reject()
    {
        if (Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Request already processed.");
        Status = FriendRequestStatus.Rejected;
        Touch();
    }

    public void Cancel()
    {
        if (Status != FriendRequestStatus.Pending)
            throw new ConflictException(ErrorCodes.Social.FriendRequestAlreadyProcessed, "Request already processed.");
        Status = FriendRequestStatus.Cancelled;
        Touch();
    }
}
