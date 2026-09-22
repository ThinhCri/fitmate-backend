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
        public const string CannotLockLastAdmin = "FITMATE.IDENTITY.CANNOT_LOCK_LAST_ADMIN";
        public const string NotAuthenticated = "FITMATE.IDENTITY.NOT_AUTHENTICATED";
        public const string HasActiveBookings = "FITMATE.IDENTITY.HAS_ACTIVE_BOOKINGS";
        public const string DeletionPending = "FITMATE.IDENTITY.DELETION_PENDING";
        public const string DeletionNotPending = "FITMATE.IDENTITY.DELETION_NOT_PENDING";
        public const string InvalidToken = "FITMATE.IDENTITY.INVALID_TOKEN";
    }

    public static class Trainer
    {
        public const string CertificateNotPending = "FITMATE.TRAINER.CERTIFICATE_NOT_PENDING";
        public const string CertificateMissingReason = "FITMATE.TRAINER.MISSING_REASON";
        public const string CannotSelfReview = "FITMATE.TRAINER.CANNOT_SELF_REVIEW";
        public const string BookingNotEligible = "FITMATE.TRAINER.BOOKING_NOT_ELIGIBLE";
        public const string AlreadyReviewed = "FITMATE.TRAINER.ALREADY_REVIEWED";
        public const string TrainerNotVerified = "FITMATE.TRAINER.NOT_VERIFIED";
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
        public const string ProviderUnavailable = "FITMATE.PAYMENT.PROVIDER_UNAVAILABLE";
        public const string ProviderNotConfigured = "FITMATE.PAYMENT.NOT_CONFIGURED";
        public const string InvalidAmount = "FITMATE.PAYMENT.INVALID_AMOUNT";
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
        public const string RequiredField = "FITMATE.VALIDATION.REQUIRED_FIELD";
        public const string TooLong = "FITMATE.VALIDATION.TOO_LONG";
        public const string TooShort = "FITMATE.VALIDATION.TOO_SHORT";
        public const string TooMany = "FITMATE.VALIDATION.TOO_MANY";
    }

    public static class General
    {
        public const string NotFound = "FITMATE.GENERAL.NOT_FOUND";
        public const string Conflict = "FITMATE.GENERAL.CONFLICT";
        public const string Internal = "FITMATE.GENERAL.INTERNAL";
    }

    public static class Social
    {
        public const string PostNotFound = "FITMATE.SOCIAL.POST_NOT_FOUND";
        public const string PostDeleteForbidden = "FITMATE.SOCIAL.POST_DELETE_FORBIDDEN";
        public const string AlreadyReacted = "FITMATE.SOCIAL.ALREADY_REACTED";
        public const string ReportTargetNotFound = "FITMATE.SOCIAL.REPORT_TARGET_NOT_FOUND";
        // BE-181: Friend + FriendRequest lifecycle.
        public const string FriendRequestAlreadyProcessed = "FITMATE.SOCIAL.FRIEND_REQUEST_ALREADY_PROCESSED";
        public const string FriendRequestAlreadyExists = "FITMATE.SOCIAL.FRIEND_REQUEST_ALREADY_EXISTS";
        public const string AlreadyFriends = "FITMATE.SOCIAL.ALREADY_FRIENDS";
        public const string FriendshipNotFound = "FITMATE.SOCIAL.FRIENDSHIP_NOT_FOUND";
        public const string RecipientBlocked = "FITMATE.SOCIAL.RECIPIENT_BLOCKED";
        public const string CannotSelfFriend = "FITMATE.SOCIAL.CANNOT_SELF_FRIEND";
        // BE-181 Block policy.
        public const string AlreadyBlocked = "FITMATE.SOCIAL.ALREADY_BLOCKED";
        public const string BlockNotFound = "FITMATE.SOCIAL.BLOCK_NOT_FOUND";
    }

    public static class Moderation
    {
        public const string ReportNotFound = "FITMATE.MODERATION.REPORT_NOT_FOUND";
        public const string ReportNotPending = "FITMATE.MODERATION.REPORT_NOT_PENDING";
        public const string DuplicateReport = "FITMATE.MODERATION.DUPLICATE_REPORT";
        public const string IdempotencyKeyInvalid = "FITMATE.MODERATION.IDEMPOTENCY_KEY_INVALID";
        public const string DeleteContentNotApplicable = "FITMATE.MODERATION.DELETE_CONTENT_NOT_APPLICABLE";
        public const string CertificateNotReportable = "FITMATE.MODERATION.CERTIFICATE_NOT_REPORTABLE";
        public const string InvalidResolveAction = "FITMATE.MODERATION.INVALID_RESOLVE_ACTION";
    }

    public static class Lock
    {
        public const string InvalidDuration = "FITMATE.LOCK.INVALID_DURATION";
    }

    // BE-191: friend chat error codes
    public static class Chat
    {
        public const string NotFriend = "FITMATE.CHAT.NOT_FRIEND";
        public const string Blocked = "FITMATE.CHAT.BLOCKED";
        public const string CannotSelfMessage = "FITMATE.CHAT.CANNOT_SELF_MESSAGE";
        public const string ConversationNotFound = "FITMATE.CHAT.CONVERSATION_NOT_FOUND";
        public const string NotParticipant = "FITMATE.CHAT.NOT_PARTICIPANT";
        public const string MessageNotFound = "FITMATE.CHAT.MESSAGE_NOT_FOUND";
        public const string IdempotencyMismatch = "FITMATE.CHAT.IDEMPOTENCY_MISMATCH";
        public const string EmptyText = "FITMATE.CHAT.EMPTY_TEXT";
        public const string TextTooLong = "FITMATE.CHAT.TEXT_TOO_LONG";
    }
}
