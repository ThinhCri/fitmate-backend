namespace Fitmate.Application.Payments;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>
    /// Explicit flag to allow mock payment provider (StubPaymentProvider).
    /// Default is false. MUST be explicitly set to true in Dev/Test environments.
    /// In Production, setting this to true is rejected or forbidden.
    /// </summary>
    public bool AllowMockProvider { get; set; } = false;

    public string Provider { get; set; } = "payos";
}
