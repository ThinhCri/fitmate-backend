namespace Fitmate.Application.Capabilities;

public record AiCapabilitiesStatus(
    bool Enabled,
    bool FoodScan,
    bool CompanionChat,
    bool WorkoutAdjust,
    string StatusReason);

public record SystemCapabilitiesDto(
    string ApiVersion,
    string Environment,
    AiCapabilitiesStatus Ai,
    bool PushNotificationsEnabled,
    bool PaymentsEnabled,
    string StorageProvider,
    PlatformPoliciesSummary Policies,
    PublicContactInfo Contact);

public record PlatformPoliciesSummary(
    int PlatformFeeBps,
    int MinCancelHours,
    int RefundWindowDays);

public record PublicContactInfo(
    string SupportEmail,
    string Hotline,
    string TermsUrl,
    string PrivacyUrl,
    string DeletionPolicyUrl);
