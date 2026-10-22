namespace Fitmate.Infrastructure.Auth;

public sealed class SpeedSmsOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; set; } = "None";
    public string AccessToken { get; set; } = string.Empty;
    public int SmsType { get; set; } = 4;
    public string Sender { get; set; } = string.Empty;

    public bool IsConfigured() =>
        Provider.Equals("SpeedSms", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !AccessToken.StartsWith("__SET_", StringComparison.Ordinal) &&
        (SmsType is 2 or 4 || (SmsType == 3 && !string.IsNullOrWhiteSpace(Sender)));
}
