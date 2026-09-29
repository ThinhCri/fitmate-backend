namespace Fitmate.Application.Auth.Contracts;

/// <summary>
/// Đăng ký member hoặc PT (admin qua CLI bootstrap, không qua API).
/// Theo OpenAPI <c>RegisterRequest</c> schema.
/// </summary>
public record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    string Role,
    decimal? HeightCm,
    decimal? WeightKg,
    string? Gender,
    DateOnly? Birthday,
    string? FitnessGoal,
    List<string>? TrainingDays,
    string? Specialty,
    int? ExperienceYears,
    long? PricePerSessionVnd,
    string? Timezone);

public record LoginRequest(string Email, string Password, string? DeviceLabel, string? Code = null, bool UseRecoveryCode = false);

public record RefreshRequest(string RefreshToken);
public record VerifyAdminLoginRequest(string ChallengeId, string Code, bool UseRecoveryCode = false);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword, string? Email = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record LogoutRequest(string? RefreshToken = null);

public record PasswordResetPayload(Guid UserId, string Email, string Token, string FullName);

public record UserSummaryDto(
    Guid Id,
    string Email,
    string? AvatarUrl,
    string FullName,
    string Role);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    UserSummaryDto User);
