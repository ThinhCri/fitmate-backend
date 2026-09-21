namespace Fitmate.Application.Payments;

public static class PaymentErrorCodes
{
    public const string HoldNotFound = "FITMATE.PAYMENT.HOLD_NOT_FOUND";
    public const string HoldExpired = "FITMATE.PAYMENT.HOLD_EXPIRED";
    public const string NotOwner = "FITMATE.PAYMENT.NOT_OWNER";
    public const string PaymentNotFound = "FITMATE.PAYMENT.PAYMENT_NOT_FOUND";
}
