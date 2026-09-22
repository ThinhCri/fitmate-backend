namespace Fitmate.Application.Payments;

public static class PaymentErrorCodes
{
    public const string HoldNotFound = "FITMATE.PAYMENT.HOLD_NOT_FOUND";
    public const string HoldExpired = "FITMATE.PAYMENT.HOLD_EXPIRED";
    public const string NotOwner = "FITMATE.PAYMENT.NOT_OWNER";
    public const string PaymentNotFound = "FITMATE.PAYMENT.PAYMENT_NOT_FOUND";
    public const string ProviderUnavailable = "FITMATE.PAYMENT.PROVIDER_UNAVAILABLE";
    public const string NotConfigured = "FITMATE.PAYMENT.NOT_CONFIGURED";
    public const string InvalidAmount = "FITMATE.PAYMENT.INVALID_AMOUNT";
    public const string BadSignature = "FITMATE.PAYMENT.BAD_SIGNATURE";
}
