namespace Fitmate.Api.Errors;

/// <summary>
/// Stable error codes cho <c>ProblemDetails.code</c> field.
/// Quy ước format: <c>FITMATE.&lt;DOMAIN&gt;.&lt;REASON&gt;</c> (UPPER_SNAKE_CASE).
/// Xem <c>docs/backend-release/api-contract.md §6</c>.
/// Không bao giờ đổi code đã phát hành; thêm code mới khi cần.
/// </summary>
public static class ErrorCodes
{
    // GENERAL
    public const string GeneralInternal = "FITMATE.GENERAL.INTERNAL";
    public const string GeneralNotFound = "FITMATE.GENERAL.NOT_FOUND";
    public const string GeneralBadRequest = "FITMATE.GENERAL.BAD_REQUEST";
    public const string GeneralRateLimited = "FITMATE.GENERAL.RATE_LIMITED";

    // VALIDATION
    public const string ValidationFailed = "FITMATE.VALIDATION.FAILED";

    // AUTH (placeholder, sẽ mở rộng ở BE-030)
    public const string AuthUnauthorized = "FITMATE.AUTH.UNAUTHORIZED";
    public const string AuthForbidden = "FITMATE.AUTH.FORBIDDEN";
    public const string AuthEmailNotVerified = "FITMATE.AUTH.EMAIL_NOT_VERIFIED";

    // BOOKING (placeholder, sẽ mở rộng ở BE-060)
    public const string BookingInvalidTransition = "FITMATE.BOOKING.INVALID_TRANSITION";
    public const string BookingSlotTaken = "FITMATE.BOOKING.SLOT_TAKEN";

    // PAYMENT (placeholder, sẽ mở rộng ở BE-140/BE-400)
    public const string PaymentProviderNotSupported = "FITMATE.PAYMENT.PROVIDER_NOT_SUPPORTED";

    // IDEMPOTENCY (placeholder, sẽ mở rộng ở BE-070)
    public const string IdempotencyConflict = "FITMATE.IDEMPOTENCY.CONFLICT";
    public const string IdempotencyInProgress = "FITMATE.IDEMPOTENCY.IN_PROGRESS";
}
