namespace Fitmate.Application.Preferences;

public record UserPreferenceDto(
    Guid UserId,
    string Language,
    string Theme,
    string Timezone,
    bool NotificationsEnabled,
    bool EmailNotifications,
    bool PushNotifications,
    bool HealthDataSharingConsent,
    bool BiometricLoginEnabled,
    DateTime UpdatedAt);

public record UpdateUserPreferenceRequest(
    string? Language,
    string? Theme,
    string? Timezone,
    bool? NotificationsEnabled,
    bool? EmailNotifications,
    bool? PushNotifications,
    bool? HealthDataSharingConsent,
    bool? BiometricLoginEnabled);

