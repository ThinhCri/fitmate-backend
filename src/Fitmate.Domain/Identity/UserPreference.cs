using Fitmate.Domain.Common;

namespace Fitmate.Domain.Identity;

/// <summary>
/// BE-819 / BR-15: User preferences entity.
/// Stores application settings and consents so they persist across logins and devices.
/// </summary>
public class UserPreference : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Language { get; private set; } = "vi";
    public string Theme { get; private set; } = "system";
    public string Timezone { get; private set; } = "Asia/Ho_Chi_Minh";
    public bool NotificationsEnabled { get; private set; } = true;
    public bool EmailNotifications { get; private set; } = true;
    public bool PushNotifications { get; private set; } = true;
    public bool HealthDataSharingConsent { get; private set; } = false;
    public bool BiometricLoginEnabled { get; private set; } = false;
    public int? TargetCalories { get; private set; }
    public int? TargetProteinG { get; private set; }
    public int? TargetCarbsG { get; private set; }
    public int? TargetFatG { get; private set; }
    public decimal? TargetWeightKg { get; private set; }
    public int? MonthlyWorkoutTarget { get; private set; }

    private UserPreference() { }

    public static UserPreference CreateDefault(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Language = "vi",
        Theme = "system",
        Timezone = "Asia/Ho_Chi_Minh",
        NotificationsEnabled = true,
        EmailNotifications = true,
        PushNotifications = true,
        HealthDataSharingConsent = false,
        BiometricLoginEnabled = false,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void Update(
        string? language = null,
        string? theme = null,
        string? timezone = null,
        bool? notificationsEnabled = null,
        bool? emailNotifications = null,
        bool? pushNotifications = null,
        bool? healthDataSharingConsent = null,
        bool? biometricLoginEnabled = null,
        int? targetCalories = null,
        int? targetProteinG = null,
        int? targetCarbsG = null,
        int? targetFatG = null,
        decimal? targetWeightKg = null,
        int? monthlyWorkoutTarget = null)
    {
        if (!string.IsNullOrWhiteSpace(language)) Language = language.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(theme)) Theme = theme.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(timezone)) Timezone = timezone.Trim();
        if (notificationsEnabled.HasValue) NotificationsEnabled = notificationsEnabled.Value;
        if (emailNotifications.HasValue) EmailNotifications = emailNotifications.Value;
        if (pushNotifications.HasValue) PushNotifications = pushNotifications.Value;
        if (healthDataSharingConsent.HasValue) HealthDataSharingConsent = healthDataSharingConsent.Value;
        if (biometricLoginEnabled.HasValue) BiometricLoginEnabled = biometricLoginEnabled.Value;
        if (targetCalories.HasValue) TargetCalories = targetCalories.Value;
        if (targetProteinG.HasValue) TargetProteinG = targetProteinG.Value;
        if (targetCarbsG.HasValue) TargetCarbsG = targetCarbsG.Value;
        if (targetFatG.HasValue) TargetFatG = targetFatG.Value;
        if (targetWeightKg.HasValue) TargetWeightKg = targetWeightKg.Value;
        if (monthlyWorkoutTarget.HasValue) MonthlyWorkoutTarget = monthlyWorkoutTarget.Value;

        Touch();
    }
}

