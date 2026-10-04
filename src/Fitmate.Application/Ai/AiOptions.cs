namespace Fitmate.Application.Ai;

/// <summary>
/// BE-817: Centralized AI capabilities configuration.
/// Default state is strictly DISABLED (kill-switch), awaiting model artifact handoff per ai-contract.md.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Master kill-switch for all AI inference features. Disabled by default.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Controls food recognition scan capability.
    /// </summary>
    public bool FoodScanEnabled { get; set; } = false;

    /// <summary>
    /// Controls AI Companion chat capability.
    /// </summary>
    public bool CompanionChatEnabled { get; set; } = false;

    /// <summary>
    /// Controls workout suggestions / plan adjustment capability.
    /// </summary>
    public bool WorkoutAdjustEnabled { get; set; } = false;

    /// <summary>
    /// Configurable timeout for AI inference requests (in seconds). Default 30s.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum allowed input image size in bytes for food scan (default 10MB).
    /// </summary>
    public long MaxImageSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Active provider or adapter strategy (e.g. "stub", "torchscript", "python_service").
    /// </summary>
    public string Provider { get; set; } = "stub";

    /// <summary>
    /// Provider for AI Companion chat: "stub", "gemini", or "openrouter".
    /// Kept separate from Provider because nutrition inference uses its own runtime.
    /// </summary>
    public string CompanionProvider { get; set; } = "stub";

    /// <summary>
    /// Optional external path or URI to the artifact/model file, configured outside the source tree.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Base URL of the separately deployed inference service when Provider is
    /// "python_service". Keep this value outside source control.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Shared bearer token used only for backend-to-inference authentication.
    /// It must never be exposed to the mobile or admin clients.
    /// </summary>
    public string? InternalToken { get; set; }
}

