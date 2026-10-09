namespace Fitmate.Infrastructure.Trainers;

public sealed class GoogleMeetOptions
{
    public const string SectionName = "GoogleMeet";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;

    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(ClientId) && !ClientId.StartsWith("__SET_", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(ClientSecret) && !ClientSecret.StartsWith("__SET_", StringComparison.Ordinal) &&
        Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
