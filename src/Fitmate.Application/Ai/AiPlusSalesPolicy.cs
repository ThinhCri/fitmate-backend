namespace Fitmate.Application.Ai;

/// <summary>AI Plus currently sells chat and workout suggestions, not food scanning.</summary>
public static class AiPlusSalesPolicy
{
    public static bool CanOpen(AiOptions ai, AiUsageOptions usage, bool companionProviderReady) =>
        ai.Enabled && ai.CompanionChatEnabled && ai.WorkoutAdjustEnabled && companionProviderReady &&
        usage.Enabled && usage.FreeChatPerDay > 0 && usage.PlusChatPerDay > usage.FreeChatPerDay;
    // Presentation compatibility only; never used to grant entitlement or trust a purchase.
    public static AiSubscriptionView ForClient(AiSubscriptionView view, string? build, int minimumBuild) =>
        int.TryParse(build, out var code) && code >= minimumBuild
            ? view : view with { BillingAvailable = false };
}

