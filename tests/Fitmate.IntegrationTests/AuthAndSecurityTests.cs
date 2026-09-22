using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Application.Auth.Contracts;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Fitmate.IntegrationTests;

public class AuthAndSecurityTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public AuthAndSecurityTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Unauthenticated_Access_To_Protected_Endpoint_Returns_401_Unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/me/nutrition/meals");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Member_Accessing_Admin_Users_Returns_403_Forbidden()
    {
        // Member role trying to access admin endpoint
        var client = _factory.CreateAuthenticatedClient(role: "Member");
        var response = await client.GetAsync("/api/v1/admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_Accessing_Admin_Users_Passes_Authorization()
    {
        var client = _factory.CreateAuthenticatedClient(role: "Admin");
        var response = await client.GetAsync("/api/v1/admin/users");

        // Should NOT be 401 or 403
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_With_Invalid_Credentials_Returns_401_ProblemDetails()
    {
        var client = _factory.CreateClient();
        var body = new LoginRequest("nonexistent_user@fitmate.vn", "WrongPassword!123", null);

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", body);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        doc!.RootElement.GetProperty("status").GetInt32().Should().Be(401);
    }
}
