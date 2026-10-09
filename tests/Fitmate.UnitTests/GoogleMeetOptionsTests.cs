using Fitmate.Infrastructure.Trainers;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

public sealed class GoogleMeetOptionsTests
{
    [Theory]
    [InlineData("https://api.fitmate.vn/api/v1/integrations/google-meet/callback", true)]
    [InlineData("http://localhost:8080/api/v1/integrations/google-meet/callback", true)]
    [InlineData("http://api.fitmate.vn/api/v1/integrations/google-meet/callback", false)]
    [InlineData("javascript:alert(1)", false)]
    public void RequiresSafeRedirectAndCredentials(string redirect, bool expected)
    {
        new GoogleMeetOptions
        {
            ClientId = "google-client-id",
            ClientSecret = "google-client-secret",
            RedirectUri = redirect
        }.IsConfigured().Should().Be(expected);
    }

    [Fact]
    public void RejectsPlaceholderCredentials()
    {
        new GoogleMeetOptions
        {
            ClientId = "__SET_CLIENT_ID__",
            ClientSecret = "secret",
            RedirectUri = "https://api.fitmate.vn/api/v1/integrations/google-meet/callback"
        }.IsConfigured().Should().BeFalse();
    }
}
