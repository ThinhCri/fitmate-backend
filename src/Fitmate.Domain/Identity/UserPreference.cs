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
        bool? biometricLoginEnabled = null)
    {
        if (!string.IsNullOrWhiteSpace(language)) Language = language.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(theme)) Theme = theme.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(timezone)) Timezone = timezone.Trim();
        if (notificationsEnabled.HasValue) NotificationsEnabled = notificationsEnabled.Value;
        if (emailNotifications.HasValue) EmailNotifications = emailNotifications.Value;
        if (pushNotifications.HasValue) PushNotifications = pushNotifications.Value;
        if (healthDataSharingConsent.HasValue) HealthDataSharingConsent = healthDataSharingConsent.Value;
        if (biometricLoginEnabled.HasValue) BiometricLoginEnabled = biometricLoginEnabled.Value;

        Touch();
    }
}

