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

    [Fact]
    public async Task WorkoutPlanNumericLimits_RejectDirectApiBypassAndPreserveSavedPlan()
    {
        using var member = factory.CreateAuthenticatedClient("Member", Guid.NewGuid());
        object Payload(int reps) => new { day_of_week = "Mon", workout_name = "Boundary API", duration_minutes = 30,
            target_calories_burn = 0, exercises = new[] { new { name = "Squat", type = "reps", sets = 10, reps, rest_sec = 600, calories = 0 } } };
        using var invalid = await member.PostAsJsonAsync("/api/v1/me/workout/plans", Payload(100000));
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var created = await member.PostAsJsonAsync("/api/v1/me/workout/plans", Payload(100));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = json.GetProperty("id").GetGuid();
        using var patch = await member.PatchAsJsonAsync($"/api/v1/me/workout/plans/{id}", Payload(101));
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var saved = await member.GetAsync($"/api/v1/me/workout/plans/{id}");
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await saved.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        detail.GetProperty("exercises")[0].GetProperty("reps").GetInt32().Should().Be(100);
    }
}
