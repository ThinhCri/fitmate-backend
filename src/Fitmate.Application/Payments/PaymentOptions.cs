namespace Fitmate.Application.Payments;

public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>
    /// Legacy switch retained only to reject unsafe old configuration on startup.
    /// True is rejected in every runtime environment; test doubles are test-only DI.
    /// </summary>
    public bool AllowMockProvider { get; set; } = false;

    public string Provider { get; set; } = "payos";
}
