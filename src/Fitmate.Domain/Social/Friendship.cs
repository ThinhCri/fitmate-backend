using Fitmate.Domain.Common;

namespace Fitmate.Domain.Social;

/// <summary>
/// BE-181: friendship là quan hệ 2 chiều, lưu 1 row canonical duy nhất cho mỗi cặp
/// (UserLowId, UserHighId) với UserLowId &lt; UserHighId. Application layer phải
/// dùng <see cref="CanonicalPair"/> để chuẩn hóa trước khi insert; DB có CHECK
/// constraint phụ để từ chối row nghịch trận.
/// </summary>
public class Friendship : AuditableEntity
{
    public Guid UserLowId { get; private set; }
    public Guid UserHighId { get; private set; }

    private Friendship() { }

    public static (Guid low, Guid high) CanonicalPair(Guid a, Guid b)
    {
        if (a == b) throw new ArgumentException("Cannot create friendship with self.", nameof(a));
        return a.CompareTo(b) < 0 ? (a, b) : (b, a);
    }

    public static Friendship Create(Guid a, Guid b)
    {
        var (low, high) = CanonicalPair(a, b);
        return new Friendship
        {
            UserLowId = low,
            UserHighId = high,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>True nếu cặp (a,b) canonical khớp row này.</summary>
    public bool Matches(Guid a, Guid b) => MatchesPair(CanonicalPair(a, b));

    private bool MatchesPair((Guid low, Guid high) pair)
        => UserLowId == pair.low && UserHighId == pair.high;
}
