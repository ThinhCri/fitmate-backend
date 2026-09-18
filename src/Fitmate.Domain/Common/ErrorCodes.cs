namespace Fitmate.Domain.Common;

/// <summary>
/// Domain error code ổn định, dạng FITMATE.&lt;DOMAIN&gt;.&lt;REASON&gt;.
/// Map sang HTTP status ở API layer.
/// </summary>
public static class ErrorCodes
{
    public static class Auth
    {
        public const string InvalidCredentials = "FITMATE.AUTH.INVALID_CREDENTIALS";
        public const string AccountLocked = "FITMATE.AUTH.ACCOUNT_LOCKED";
        public const string EmailTaken = "FITMATE.AUTH.EMAIL_TAKEN";
        public const string WeakPassword = "FITMATE.AUTH.WEAK_PASSWORD";
        public const string TokenExpired = "FITMATE.AUTH.TOKEN_EXPIRED";
        public const string TokenInvalid = "FITMATE.AUTH.TOKEN_INVALID";
    }

    public static class Identity
    {
        public const string UserNotFound = "FITMATE.IDENTITY.USER_NOT_FOUND";
        public const string Forbidden = "FITMATE.IDENTITY.FORBIDDEN";
        public const string CannotSelfElevate = "FITMATE.IDENTITY.CANNOT_SELF_ELEVATE";
    }

    public static class Trainer
    {
        public const string CertificateNotPending = "FITMATE.TRAINER.CERTIFICATE_NOT_PENDING";
        public const string CertificateMissingReason = "FITMATE.TRAINER.MISSING_REASON";
    }

    public static class Booking
    {
        public const string SlotTaken = "FITMATE.BOOKING.SLOT_TAKEN";
        public const string HoldExpired = "FITMATE.BOOKING.HOLD_EXPIRED";
        public const string PaymentNotPaid = "FITMATE.BOOKING.PAYMENT_NOT_PAID";
        public const string CannotTransition = "FITMATE.BOOKING.CANNOT_TRANSITION";
        public const string NotOwner = "FITMATE.BOOKING.NOT_OWNER";
    }

    public static class Payment
    {
        public const string BadSignature = "FITMATE.PAYMENT.BAD_SIGNATURE";
        public const string AlreadyProcessed = "FITMATE.PAYMENT.ALREADY_PROCESSED";
        public const string RefundCapExceeded = "FITMATE.PAYMENT.REFUND_CAP_EXCEEDED";
    }

    public static class Idempotency
    {
        public const string Conflict = "FITMATE.IDEMPOTENCY.CONFLICT";
    }

    public static class AI
    {
        public const string Unavailable = "FITMATE.AI.UNAVAILABLE";
        public const string InvalidOutput = "FITMATE.AI.INVALID_OUTPUT";
        public const string RateLimited = "FITMATE.AI.RATE_LIMITED";
        public const string QuotaExceeded = "FITMATE.AI.QUOTA_EXCEEDED";
    }

    public static class Validation
    {
        public const string FieldInvalid = "FITMATE.VALIDATION.FIELD_INVALID";
    }

    public static class General
    {
        public const string NotFound = "FITMATE.GENERAL.NOT_FOUND";
        public const string Internal = "FITMATE.GENERAL.INTERNAL";
    }
}
