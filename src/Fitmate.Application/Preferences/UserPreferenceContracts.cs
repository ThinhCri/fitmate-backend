using System.Text.Json.Serialization;

namespace Fitmate.Application.Preferences;

public record UserPreferenceDto(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("notifications_enabled")] bool NotificationsEnabled,
    [property: JsonPropertyName("email_notifications")] bool EmailNotifications,
    [property: JsonPropertyName("push_notifications")] bool PushNotifications,
    [property: JsonPropertyName("health_data_sharing_consent")] bool HealthDataSharingConsent,
    [property: JsonPropertyName("biometric_login_enabled")] bool BiometricLoginEnabled,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt,
    [property: JsonPropertyName("target_calories")] int? TargetCalories = null,
    [property: JsonPropertyName("target_protein_g")] int? TargetProteinG = null,
    [property: JsonPropertyName("target_carbs_g")] int? TargetCarbsG = null,
    [property: JsonPropertyName("target_fat_g")] int? TargetFatG = null);

public record UpdateUserPreferenceRequest(
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("theme")] string? Theme,
    [property: JsonPropertyName("timezone")] string? Timezone,
    [property: JsonPropertyName("notifications_enabled")] bool? NotificationsEnabled,
    [property: JsonPropertyName("email_notifications")] bool? EmailNotifications,
    [property: JsonPropertyName("push_notifications")] bool? PushNotifications,
    [property: JsonPropertyName("health_data_sharing_consent")] bool? HealthDataSharingConsent,
    [property: JsonPropertyName("biometric_login_enabled")] bool? BiometricLoginEnabled,
    [property: JsonPropertyName("target_calories")] int? TargetCalories = null,
    [property: JsonPropertyName("target_protein_g")] int? TargetProteinG = null,
    [property: JsonPropertyName("target_carbs_g")] int? TargetCarbsG = null,
    [property: JsonPropertyName("target_fat_g")] int? TargetFatG = null);
