namespace Fitmate.Application.Common;

/// <summary>
/// Money VND helpers. Integer VND, half-up rounding.
///
/// <para>Quy ước:
/// <list type="bullet">
/// <item>Integer VND only (DEC-006). Không decimal/float cho storage.</item>
/// <item>Phí nền tảng lưu theo basis points (DEC-029). <c>1000 bps = 10.00%</c>.</item>
/// <item>Refund lũy kế (AGENTS.md §"Nguyên tắc bắt buộc"): tổng refund ≤ actual_paid.</item>
/// <item>Half-up rounding đồng gần nhất (AGENTS.md §"Nguyên tắc bắt buộc").</item>
/// </list>
/// </para>
/// </summary>
public static class Money
{
    /// <summary>
    /// Half-up rounding: 0.5 → 1. Dùng cho mọi tính tiền VND.
    /// </summary>
    public static long RoundVnd(decimal value)
    {
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Tính fee Fitmate và phần PT theo tỷ lệ decimal (0.10 = 10%).
    /// </summary>
    /// <remarks>
    /// Vẫn giữ cho backward-compat với code cũ. Các module mới nên dùng <see cref="SplitFeeBps"/>.
    /// </remarks>
    public static (long platformFee, long trainerShare) SplitFee(long actualPaidVnd, decimal feeRate)
    {
        if (actualPaidVnd < 0) throw new ArgumentOutOfRangeException(nameof(actualPaidVnd));
        if (feeRate < 0m || feeRate > 1m) throw new ArgumentOutOfRangeException(nameof(feeRate));

        var fee = RoundVnd(actualPaidVnd * feeRate);
        var share = actualPaidVnd - fee;
        return (fee, share);
    }

    /// <summary>
    /// Tính hoàn: actual - alreadyRefunded. Không vượt quá total.
    /// </summary>
    public static long ApplyRefund(long totalPaidVnd, long alreadyRefundedVnd, long refundThisTimeVnd)
    {
        if (refundThisTimeVnd <= 0) throw new ArgumentOutOfRangeException(nameof(refundThisTimeVnd));
        var newTotal = alreadyRefundedVnd + refundThisTimeVnd;
        if (newTotal > totalPaidVnd)
            throw new InvalidOperationException(
                $"Refund cap exceeded: total={newTotal} > paid={totalPaidVnd}");
        return newTotal;
    }

    // ===================== DEC-029: basis points =====================

    /// <summary>
    /// Validate bps thuộc [0, 10000] (0%..100%). 10000 bps = 100.00%.
    /// </summary>
    public static void ValidateBps(int platformFeeRateBps)
    {
        if (platformFeeRateBps < 0 || platformFeeRateBps > 10_000)
            throw new ArgumentOutOfRangeException(nameof(platformFeeRateBps),
                $"platformFeeRateBps must be in [0,10000], got {platformFeeRateBps}");
    }

    /// <summary>
    /// Tính fee Fitmate và phần PT theo basis points (DEC-029).
    /// <c>platformFeeRateBps=1000</c> → 10.00%.
    /// </summary>
    /// <remarks>
    /// Half-up rounding: <c>SplitFeeBps(1001, 1000)</c> = (101, 900).
    /// Sum invariant: <c>platformFee + trainerShare == actualPaidVnd</c> luôn đúng
    /// vì trainerShare = actual - fee, không có rounding lần 2.
    /// </remarks>
    public static (long platformFee, long trainerShare) SplitFeeBps(long actualPaidVnd, int platformFeeRateBps)
    {
        if (actualPaidVnd < 0) throw new ArgumentOutOfRangeException(nameof(actualPaidVnd));
        ValidateBps(platformFeeRateBps);

        // fee = round(actualPaid * bps / 10000, VND)
        var fee = RoundVnd((decimal)actualPaidVnd * platformFeeRateBps / 10_000m);
        var share = actualPaidVnd - fee;
        return (fee, share);
    }

    /// <summary>
    /// Bps → string phần trăm hiển thị. <c>FormatPercent(1000)</c> → <c>"10.00%"</c>.
    /// </summary>
    public static string FormatPercent(int platformFeeRateBps)
    {
        ValidateBps(platformFeeRateBps);
        // 2 chữ số thập phân, locale-invariant để log/test ổn định.
        var value = platformFeeRateBps / 100m;
        return value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    // ===================== DEC-008: refund allocation =====================

    /// <summary>
    /// Phân bổ 1 khoản refund cho cả <c>platformFee</c> + <c>trainerShare</c> theo tỷ lệ ban đầu.
    /// DEC-008: "Refund giảm cả phần fee và PT share tỷ lệ".
    /// </summary>
    /// <remarks>
    /// Ví dụ: paid 100000 → fee 10000, share 90000.
    /// Refund 30000 → fee 3000, share 27000. Sum = 30000 = refund.
    /// Refund lũy kế 2 lần: 30000 + 20000 → tổng 50000; mỗi lần đều lũy theo tỷ lệ gốc.
    /// Edge: refund = paid → fee = paidFee, share = paidShare (giải tán hoàn toàn).
    /// Edge: refund = 0 → fee = 0, share = 0.
    /// </remarks>
    public static (long refundFee, long refundShare) AllocateRefund(
        long paidFeeVnd,
        long paidShareVnd,
        long refundThisTimeVnd)
    {
        if (paidFeeVnd < 0) throw new ArgumentOutOfRangeException(nameof(paidFeeVnd));
        if (paidShareVnd < 0) throw new ArgumentOutOfRangeException(nameof(paidShareVnd));
        if (refundThisTimeVnd < 0) throw new ArgumentOutOfRangeException(nameof(refundThisTimeVnd));

        var totalPaid = paidFeeVnd + paidShareVnd;
        if (totalPaid == 0)
        {
            if (refundThisTimeVnd != 0)
                throw new InvalidOperationException("Cannot refund non-zero amount on zero-paid booking.");
            return (0, 0);
        }

        // Phân bổ tỷ lệ với half-up, đảm bảo fee + share == refund (sum invariant).
        var fee = RoundVnd((decimal)refundThisTimeVnd * paidFeeVnd / totalPaid);
        var share = refundThisTimeVnd - fee;
        return (fee, share);
    }

    /// <summary>
    /// Helper: kiểm tra tổng refund lũy kế không vượt paid (AGENTS.md §"Nguyên tắc bắt buộc").
    /// Trả về tổng refund mới nếu OK; throw nếu vượt cap.
    /// </summary>
    public static long ApplyCumulativeRefund(long totalPaidVnd, long alreadyRefundedVnd, long refundThisTimeVnd)
    {
        // Delegate cho ApplyRefund (giữ 1 entry point).
        return ApplyRefund(totalPaidVnd, alreadyRefundedVnd, refundThisTimeVnd);
    }

    // ===================== DEC-029: snapshot =====================

    /// <summary>
    /// Snapshot tiền cho 1 booking. Lưu DB cùng booking để không tính lại khi đổi config (DEC-029).
    /// </summary>
    public sealed record MoneySnapshot(
        long PriceVnd,
        long DiscountVnd,
        long ActualPaidVnd,
        int PlatformFeeRateBps,
        long PlatformFeeVnd,
        long TrainerShareVnd,
        string PolicyVersion)
    {
        /// <summary>
        /// Sum invariant: <c>platformFee + trainerShare == actualPaid</c> (DEC-008).
        /// </summary>
        public bool SumHolds() => PlatformFeeVnd + TrainerShareVnd == ActualPaidVnd;
    }

    /// <summary>
    /// Tạo snapshot từ price/discount + bps + policy version. Helper để booking use case gọi 1 lần.
    /// </summary>
    public static MoneySnapshot BuildSnapshot(
        long priceVnd,
        long discountVnd,
        int platformFeeRateBps,
        string policyVersion)
    {
        if (priceVnd < 0) throw new ArgumentOutOfRangeException(nameof(priceVnd));
        if (discountVnd < 0) throw new ArgumentOutOfRangeException(nameof(discountVnd));
        if (discountVnd > priceVnd)
            throw new ArgumentOutOfRangeException(nameof(discountVnd),
                $"discount ({discountVnd}) cannot exceed price ({priceVnd})");
        if (string.IsNullOrWhiteSpace(policyVersion))
            throw new ArgumentException("policyVersion must not be empty", nameof(policyVersion));

        ValidateBps(platformFeeRateBps);

        var actualPaid = priceVnd - discountVnd;
        var (fee, share) = SplitFeeBps(actualPaid, platformFeeRateBps);

        return new MoneySnapshot(
            PriceVnd: priceVnd,
            DiscountVnd: discountVnd,
            ActualPaidVnd: actualPaid,
            PlatformFeeRateBps: platformFeeRateBps,
            PlatformFeeVnd: fee,
            TrainerShareVnd: share,
            PolicyVersion: policyVersion);
    }
}
