using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Fitmate.Application.Capabilities;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Fitmate.IntegrationTests;

public class CapabilitiesAndHealthTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public CapabilitiesAndHealthTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Health_Live_Returns_200_OK()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_Ready_Returns_200_OK_With_Real_Database()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Root_Returns_Service_Metadata()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        doc.Should().NotBeNull();
        doc!.RootElement.GetProperty("name").GetString().Should().Be("Fitmate API");
        doc.RootElement.GetProperty("version").GetString().Should().Be("1.0.0");
    }

    [Fact]
    public async Task System_Capabilities_Returns_Truthful_Ai_Status_And_Public_Policies()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/system/capabilities");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<SystemCapabilitiesDto>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        dto.Should().NotBeNull();
        dto!.Ai.Enabled.Should().BeFalse();
        dto.Ai.FoodScan.Should().BeFalse();
        dto.Ai.CompanionChat.Should().BeFalse();
        dto.Ai.WorkoutAdjust.Should().BeFalse();
        dto.Ai.StatusReason.Should().Contain("BE-817");

        dto.Policies.PlatformFeeBps.Should().Be(1000);
        dto.Policies.MinCancelHours.Should().Be(48);
        dto.Policies.RefundWindowDays.Should().Be(7);

        dto.VipPurchase.Should().NotBeNull();
        dto.VipPurchase.Enabled.Should().BeFalse();
        dto.VipPurchase.StatusReason.Should().NotBeNullOrWhiteSpace();

        dto.RewardCashout.Should().NotBeNull();
        dto.RewardCashout.Enabled.Should().BeFalse();
        dto.RewardCashout.StatusReason.Should().NotBeNullOrWhiteSpace();

        dto.PtAutomaticPayout.Should().NotBeNull();
        dto.PtAutomaticPayout.Enabled.Should().BeFalse();
        dto.PtAutomaticPayout.StatusReason.Should().NotBeNullOrWhiteSpace();

        dto.Contact.SupportEmail.Should().Be("support@fitmate.vn");
        dto.Contact.Hotline.Should().NotBeNullOrWhiteSpace();

        // Security check: verify no secrets leaked in response JSON
        var rawJson = await response.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("Password");
        rawJson.Should().NotContain("SigningKey");
        rawJson.Should().NotContain("Secret");
    }
}

