using System.Text.Json.Serialization;

namespace Fitmate.Application.Identity;

public sealed record AccountDeletionRequest(
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("password")] string? Password,
    [property: JsonPropertyName("otp")] string? Otp = null);

public sealed record DeletionOtpStartResult(
    [property: JsonPropertyName("expires_at")] DateTime ExpiresAt,
    [property: JsonPropertyName("resend_after_seconds")] int ResendAfterSeconds);

public sealed record AccountDeletionStatusDto(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_pending_deletion")] bool IsPendingDeletion,
    [property: JsonPropertyName("requested_at")] DateTime? RequestedAt,
    [property: JsonPropertyName("scheduled_permanent_deletion_at")] DateTime? ScheduledPermanentDeletionAt,
    [property: JsonPropertyName("retention_days_remaining")] int RetentionDaysRemaining,
    [property: JsonPropertyName("has_active_bookings")] bool HasActiveBookings);

public sealed record PublicAccountDeletionRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record PublicAccountDeletionConfirm(
    [property: JsonPropertyName("token")] string Token);

public sealed record UserDataExportDto(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("profile")] UserProfileDto? Profile,
    [property: JsonPropertyName("exported_at")] DateTime ExportedAtUtc);

