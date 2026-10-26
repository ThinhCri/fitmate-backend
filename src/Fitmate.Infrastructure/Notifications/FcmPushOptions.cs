namespace Fitmate.Infrastructure.Notifications;

public sealed class FcmPushOptions
{
    public const string SectionName = "FcmPush";
    public bool Enabled { get; set; }
    public string ProjectId { get; set; } = "";
    public string ServiceAccountJsonBase64 { get; set; } = "";
}
