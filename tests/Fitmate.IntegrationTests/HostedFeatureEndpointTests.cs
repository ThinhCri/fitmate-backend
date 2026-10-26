using System.Net;
using System.Net.Http.Json;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Fitmate.IntegrationTests;

public sealed class HostedFeatureEndpointTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory factory;
    public HostedFeatureEndpointTests(FitmateTestAppFactory factory) { this.factory = factory; factory.EnsureDatabaseInitialized(); }
    [Fact]
    public async Task Suggestions_RequireAuthenticationAndValidateInputWithoutInference()
    {
        using var anonymous = factory.CreateClient();
        using var denied = await anonymous.PostAsJsonAsync("/api/v1/me/workout/suggestions", new { goal = "vận động" });
        denied.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var member = factory.CreateAuthenticatedClient("Member", Guid.NewGuid());
        using var invalid = await member.PostAsJsonAsync("/api/v1/me/workout/suggestions", new { goal = "" });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    [Fact]
    public async Task ProfileSummary_DoesNotExposeOthersWorkoutCount()
    {
        var owner = Guid.NewGuid(); using var ownClient = factory.CreateAuthenticatedClient("Member", owner);
        using var viewer = factory.CreateAuthenticatedClient("Member", Guid.NewGuid());
        using var response = await viewer.GetAsync($"/api/v1/social/users/{owner}/summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        json.GetProperty("completed_workouts_count").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }
}
