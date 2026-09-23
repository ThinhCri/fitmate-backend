using Fitmate.Domain.Common;

namespace Fitmate.Application.Capabilities;

/// <summary>
/// BE-829 / BR-17: Rào chắn Application-level bảo vệ các tính năng đang đóng băng trong backlog.
/// Ngăn chặn việc bypass giao diện để gọi trực tiếp các hành vi:
/// - Mua gói VIP qua cổng ngoài (chưa có In-App Purchases).
/// - Đổi điểm thưởng thành tiền mặt (Reward points cashout).
/// - Yêu cầu tự động rút tiền trực tiếp (Automated direct PT payout).
/// </summary>
public static class BacklogFeatureGuard
{
    public const string FeatureDisabledCode = "FITMATE.FEATURE.DISABLED";

    /// <summary>
    /// Chặn việc mua gói VIP thành viên trong phiên bản hiện tại.
    /// </summary>
    public static void AssertVipPurchaseAllowed()
    {
        throw new DomainException(
            FeatureDisabledCode,
            "VIP membership purchases are currently disabled. Dedicated Google Play In-App Purchases integration will be available in a future update.",
            403);
    }

    /// <summary>
    /// Chặn việc quy đổi điểm thưởng gamification thành tiền mặt hoặc số dư tài chính.
    /// </summary>
    public static void AssertRewardCashoutAllowed()
    {
        throw new DomainException(
            FeatureDisabledCode,
            "Reward points are strictly for gamification/achievements and cannot be cashed out or converted to monetary balance.",
            403);
    }

    /// <summary>
    /// Chặn việc tự động rút tiền trực tiếp của Huấn luyện viên.
    /// </summary>
    public static void AssertAutomaticPayoutAllowed()
    {
        throw new DomainException(
            FeatureDisabledCode,
            "Automated direct payouts are not supported. Trainer earnings settlements are processed periodically by administrators via verified bank transfers.",
            403);
    }
}

