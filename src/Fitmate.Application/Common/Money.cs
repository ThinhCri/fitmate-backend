namespace Fitmate.Application.Common;

/// <summary>
/// Money VND helpers. Integer VND, half-up rounding.
/// </summary>
public static class Money
{
    /// <summary>
    /// Half-up rounding: 0.5 → 1.
    /// </summary>
    public static long RoundVnd(decimal value)
    {
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Tính fee Fitmate và phần PT.
    /// actualPaidVnd = price - discount.
    /// platformFee = round(actualPaid * rate, VND).
    /// trainerShare = actualPaid - platformFee.
    /// </summary>
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
}
