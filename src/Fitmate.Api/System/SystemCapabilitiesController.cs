using Fitmate.Application.Ai;
using Fitmate.Application.Capabilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fitmate.Api.Capabilities;

/// <summary>
/// BE-819 / BE-817 / BE-829 / BR-15 / BR-17: Public system capabilities and contact information.
/// Truthfully reflects module readiness (AI disabled until BE-817 artifact handoff;
/// VIP purchase, Reward cashout, and Automated PT payout gated until separate verified releases),
/// platform policy constants, and contact links without exposing infrastructure secrets.
/// </summary>
[ApiController]
[Route("api/v1/system/capabilities")]
[AllowAnonymous]
public class SystemCapabilitiesController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IOptions<AiOptions> _aiOptions;
    private readonly Fitmate.Application.Payments.IPaymentProvider? _payments;
    private readonly Fitmate.Application.Notifications.IPushProvider? _push;

    public SystemCapabilitiesController(IWebHostEnvironment env, IOptions<AiOptions> aiOptions,
        Fitmate.Application.Payments.IPaymentProvider? payments = null,
        Fitmate.Application.Notifications.IPushProvider? push = null)
    {
        _env = env;
        _aiOptions = aiOptions;
        _payments = payments;
        _push = push;
    }

    [HttpGet]
    public IActionResult GetCapabilities()
    {
        var ai = _aiOptions.Value;
        var capabilities = new SystemCapabilitiesDto(
            ApiVersion: "1.0.0",
            Environment: _env.EnvironmentName.ToLowerInvariant(),
            Ai: new AiCapabilitiesStatus(
                Enabled: ai.Enabled,
                FoodScan: ai.FoodScanEnabled,
                CompanionChat: ai.CompanionChatEnabled,
                WorkoutAdjust: ai.WorkoutAdjustEnabled,
                StatusReason: ai.Enabled
                    ? "AI capabilities are active."
                    : "AI capabilities are awaiting artifact delivery and validation (BE-817). Manual workout, nutrition, and chat features are fully operational."),
            VipPurchase: new FeatureCapabilityStatus(
                Enabled: false,
                StatusReason: "VIP membership purchases are not available in current release. Dedicated in-app purchase flow will be provided in a future update."),
            RewardCashout: new FeatureCapabilityStatus(
                Enabled: false,
                StatusReason: "Reward points are strictly for in-app achievements and gamification, isolated from financial ledgers and non-redeemable for cash."),
            PtAutomaticPayout: new FeatureCapabilityStatus(
                Enabled: false,
                StatusReason: "Automated direct payouts are not active. Trainer earnings are settled periodically through verified manual accounting transfers."),
            PushNotificationsEnabled: _push is not null and not Fitmate.Infrastructure.Notifications.MissingPushProvider,
            PaymentsEnabled: _payments is not null and not Fitmate.Application.Payments.MissingPaymentProvider
                and not Fitmate.Application.Payments.StubPaymentProvider,
            StorageProvider: "local",
            Policies: new PlatformPoliciesSummary(
                PlatformFeeBps: 1000,
                MinCancelHours: 48,
                RefundWindowDays: 7),
            Contact: new PublicContactInfo(
                SupportEmail: "votanthinhseven@gmail.com",
                Hotline: "",
                TermsUrl: "https://fitmate.site/terms/",
                PrivacyUrl: "https://fitmate.site/privacy/",
                DeletionPolicyUrl: "https://fitmate.site/account-deletion/"));

        return Ok(capabilities);
    }
}
