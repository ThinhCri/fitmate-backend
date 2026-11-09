namespace Fitmate.Application.Ai;

public sealed class GooglePlayBillingOptions
{
    public const string SectionName = "GooglePlayBilling";
    public bool Enabled { get; set; }
    // Separately controls new purchases. Keep verification enabled for existing buyers after sales close.
    public bool SalesEnabled { get; set; }
    // Opt in only after creating and accepting the new Play one-time product.
    public bool OneTimeSalesEnabled { get; set; }
    public string OneTimeProductId { get; set; } = "fitmate_ai_plus_month";
    public int OneTimeMinimumAndroidVersionCode { get; set; } = 8;
    public string PackageName { get; set; } = "vn.fitmate.app";
    public string ProductId { get; set; } = "fitmate_ai_plus";
    public string BasePlanId { get; set; } = "monthly";
    // Base64-encoded Google service-account JSON. Supply from a secret manager, never the app.
    public string ServiceAccountJsonBase64 { get; set; } = string.Empty;
    // Older clients advertise food scanning as a paid benefit; do not open their storefront.
    public int MinimumAndroidVersionCode { get; set; } = 5;
    public int RefreshMinutes { get; set; } = 5;
}
