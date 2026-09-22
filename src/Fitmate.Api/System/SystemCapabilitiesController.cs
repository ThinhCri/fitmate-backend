using Fitmate.Application.Ai;
using Fitmate.Application.Capabilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fitmate.Api.Capabilities;

/// <summary>
/// BE-819 / BE-817 / BR-15 / ai-contract.md: Public system capabilities and contact information.
/// Truthfully reflects module readiness (AI disabled until BE-817 artifact handoff),
/// platform policy constants, and contact links without exposing infrastructure secrets.
/// </summary>
[ApiController]
[Route("api/v1/system/capabilities")]
[AllowAnonymous]
public class SystemCapabilitiesController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IOptions<AiOptions> _aiOptions;

    public SystemCapabilitiesController(IWebHostEnvironment env, IOptions<AiOptions> aiOptions)
    {
        _env = env;
        _aiOptions = aiOptions;
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
            PushNotificationsEnabled: true,
            PaymentsEnabled: true,
            StorageProvider: "local",
            Policies: new PlatformPoliciesSummary(
                PlatformFeeBps: 1000,
                MinCancelHours: 24,
                RefundWindowDays: 7),
            Contact: new PublicContactInfo(
                SupportEmail: "support@fitmate.vn",
                Hotline: "1900-FITMATE",
                TermsUrl: "https://fitmate.vn/terms",
                PrivacyUrl: "https://fitmate.vn/privacy",
                DeletionPolicyUrl: "https://fitmate.vn/account-deletion"));

        return Ok(capabilities);
    }
}
