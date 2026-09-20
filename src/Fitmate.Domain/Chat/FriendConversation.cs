using Fitmate.Domain.Common;

namespace Fitmate.Domain.Chat;

/// <summary>
/// BE-191: 1 row canonical duy nhất cho mỗi cặp friend chat
/// (UserLowId, UserHighId) với UserLowId &lt; UserHighId. Application layer
/// phải dùng <see cref="CanonicalPair"/> để chuẩn hoá trước khi insert; DB có
/// CHECK constraint phụ để từ chối row nghịch trận (đồng bộ với Friendship ở BE-181).
///
/// Không cascade delete với Friendship: unfriend / block KHÔNG xoá messages,
/// chỉ ngăn tạo message mới (xem domain-rules §12).
/// </summary>
public class FriendConversation : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserLowId { get; private set; }
    public Guid UserHighId { get; private set; }
    public DateTime LastMessageAt { get; private set; }

    private FriendConversation() { }

    public static (Guid low, Guid high) CanonicalPair(Guid a, Guid b)
    {
        if (a == b) throw new ArgumentException("Cannot create conversation with self.", nameof(a));
        return a.CompareTo(b) < 0 ? (a, b) : (b, a);
    }

    public static FriendConversation Create(Guid a, Guid b)
    {
        var (low, high) = CanonicalPair(a, b);
        return new FriendConversation
        {
            Id = Guid.NewGuid(),
            UserLowId = low,
            UserHighId = high,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow
        };
    }

    public void TouchLastMessage()
    {
        LastMessageAt = DateTime.UtcNow;
        Touch();
    }

    public bool IsParticipant(Guid userId) =>
        userId == UserLowId || userId == UserHighId;

    public Guid OtherParticipant(Guid userId) =>
        userId == UserLowId
            ? UserHighId
            : userId == UserHighId
                ? UserLowId
                : throw new ArgumentException("User is not a participant of this conversation.", nameof(userId));
}
