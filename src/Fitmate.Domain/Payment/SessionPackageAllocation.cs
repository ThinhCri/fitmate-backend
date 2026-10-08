namespace Fitmate.Domain.Payment;

/// <summary>
/// Immutable quote allocation for a paid four/eight-session package. Slots must be
/// supplied in chronological order; the earliest slots receive any VND remainder.
/// Fee deltas use cumulative half-up rounding so their sum equals exactly 10% of
/// the amount actually collected, including prices not divisible by ten.
/// </summary>
public sealed record SessionPriceAllocation(int Sequence, long PaidVnd, long PlatformFeeVnd, long TrainerShareVnd);

public static class SessionPackageAllocation
{
    public static IReadOnlyList<SessionPriceAllocation> Allocate(long actualPaidVnd, int sessionCount)
    {
        if (sessionCount is not (4 or 8))
            throw new ArgumentOutOfRangeException(nameof(sessionCount), "Only four- and eight-session packages are supported.");
        if (actualPaidVnd < sessionCount)
            throw new ArgumentOutOfRangeException(nameof(actualPaidVnd), "Every session needs a positive allocation.");

        var totalFee = (long)Math.Round(actualPaidVnd * 0.10m, MidpointRounding.AwayFromZero);
        var baseAmount = actualPaidVnd / sessionCount;
        var remainder = actualPaidVnd % sessionCount;
        var result = new List<SessionPriceAllocation>(sessionCount);
        long cumulativePaid = 0;
        long previousCumulativeFee = 0;
        for (var index = 0; index < sessionCount; index++)
        {
            var paid = baseAmount + (index < remainder ? 1 : 0);
            cumulativePaid = checked(cumulativePaid + paid);
            var cumulativeFee = (long)Math.Round(totalFee * (decimal)cumulativePaid / actualPaidVnd,
                MidpointRounding.AwayFromZero);
            var fee = cumulativeFee - previousCumulativeFee;
            result.Add(new SessionPriceAllocation(index + 1, paid, fee, paid - fee));
            previousCumulativeFee = cumulativeFee;
        }
        return result;
    }
}
