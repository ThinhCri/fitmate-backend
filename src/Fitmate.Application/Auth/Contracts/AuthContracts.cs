using System.Text.Json.Serialization;

namespace Fitmate.Application.Auth.Contracts;

/// <summary>
/// Đăng ký member hoặc PT (admin qua CLI bootstrap, không qua API).
/// Theo OpenAPI <c>RegisterRequest</c> schema.
/// </summary>
public record RegisterRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("height_cm")] decimal? HeightCm = null,
    [property: JsonPropertyName("weight_kg")] decimal? WeightKg = null,
    [property: JsonPropertyName("gender")] string? Gender = null,
    [property: JsonPropertyName("birthday")] DateOnly? Birthday = null,
    [property: JsonPropertyName("fitness_goal")] string? FitnessGoal = null,
    [property: JsonPropertyName("training_days")] List<string>? TrainingDays = null,
    [property: JsonPropertyName("specialty")] string? Specialty = null,
    [property: JsonPropertyName("experience_years")] int? ExperienceYears = null,
    [property: JsonPropertyName("price_per_session_vnd")] long? PricePerSessionVnd = null,
    [property: JsonPropertyName("timezone")] string? Timezone = null);

public record LoginRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("device_label")] string? DeviceLabel = null,
    [property: JsonPropertyName("code")] string? Code = null,
    [property: JsonPropertyName("use_recovery_code")] bool UseRecoveryCode = false);

public record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

public record VerifyAdminLoginRequest(
    [property: JsonPropertyName("challenge_id")] string ChallengeId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("use_recovery_code")] bool UseRecoveryCode = false);

public record ForgotPasswordRequest(
    [property: JsonPropertyName("email")] string Email);

public record ResetPasswordRequest(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("new_password")] string NewPassword,
    [property: JsonPropertyName("email")] string Email);

public record ChangePasswordRequest(
    [property: JsonPropertyName("current_password")] string CurrentPassword,
    [property: JsonPropertyName("new_password")] string NewPassword);

public record VerifyEmailRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("code")] string Code);

public record ResendEmailVerificationRequest(
    [property: JsonPropertyName("email")] string Email);

public record RegistrationPendingResponse(
    [property: JsonPropertyName("verification_required")] bool VerificationRequired,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("expires_in_seconds")] int ExpiresInSeconds);

public record LogoutRequest(
    [property: JsonPropertyName("refresh_token")] string? RefreshToken = null);

public record PasswordResetPayload(Guid UserId, string Email, string Token, string FullName);

public record UserSummaryDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("pt_verification_status")] string? PtVerificationStatus = null);

public record AuthResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("user")] UserSummaryDto User);
