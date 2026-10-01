using System.Text.Json.Serialization;

namespace Fitmate.Application.Capabilities;

public record AiCapabilitiesStatus(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("food_scan")] bool FoodScan,
    [property: JsonPropertyName("companion_chat")] bool CompanionChat,
    [property: JsonPropertyName("workout_adjust")] bool WorkoutAdjust,
    [property: JsonPropertyName("status_reason")] string StatusReason);

public record FeatureCapabilityStatus(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("status_reason")] string StatusReason);

public record SystemCapabilitiesDto(
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("environment")] string Environment,
    [property: JsonPropertyName("ai")] AiCapabilitiesStatus Ai,
    [property: JsonPropertyName("vip_purchase")] FeatureCapabilityStatus VipPurchase,
    [property: JsonPropertyName("reward_cashout")] FeatureCapabilityStatus RewardCashout,
    [property: JsonPropertyName("pt_automatic_payout")] FeatureCapabilityStatus PtAutomaticPayout,
    [property: JsonPropertyName("push_notifications_enabled")] bool PushNotificationsEnabled,
    [property: JsonPropertyName("payments_enabled")] bool PaymentsEnabled,
    [property: JsonPropertyName("storage_provider")] string StorageProvider,
    [property: JsonPropertyName("policies")] PlatformPoliciesSummary Policies,
    [property: JsonPropertyName("contact")] PublicContactInfo Contact);

public record PlatformPoliciesSummary(
    [property: JsonPropertyName("platform_fee_bps")] int PlatformFeeBps,
    [property: JsonPropertyName("min_cancel_hours")] int MinCancelHours,
    [property: JsonPropertyName("refund_window_days")] int RefundWindowDays);

public record PublicContactInfo(
    [property: JsonPropertyName("support_email")] string SupportEmail,
    [property: JsonPropertyName("hotline")] string Hotline,
    [property: JsonPropertyName("terms_url")] string TermsUrl,
    [property: JsonPropertyName("privacy_url")] string PrivacyUrl,
    [property: JsonPropertyName("deletion_policy_url")] string DeletionPolicyUrl);
