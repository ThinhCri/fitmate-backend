using System.Net;
using System.Net.Http.Json;
using Fitmate.Application.Preferences;
using Fitmate.Application.Support;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace Fitmate.IntegrationTests;

public class SupportAndPreferencesTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public SupportAndPreferencesTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task UserPreferences_Roundtrip_Persists_In_Database()
    {
        var userId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(role: "Member", userId: userId);

        // 1. Initial get creates or retrieves default preferences
        var initialGet = await client.GetAsync("/api/v1/users/me/preferences");
        initialGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var initialDto = await initialGet.Content.ReadFromJsonAsync<UserPreferenceDto>();
        initialDto.Should().NotBeNull();
        initialDto!.Language.Should().Be("vi");

        // 2. Update preferences
        var updateReq = new UpdateUserPreferenceRequest(
            Language: "en",
            Theme: "dark",
            Timezone: "Asia/Bangkok",
            NotificationsEnabled: true,
            EmailNotifications: false,
            PushNotifications: true,
            HealthDataSharingConsent: true,
            BiometricLoginEnabled: false);

        var putResponse = await client.PutAsJsonAsync("/api/v1/users/me/preferences", updateReq);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedDto = await putResponse.Content.ReadFromJsonAsync<UserPreferenceDto>();
        updatedDto.Should().NotBeNull();
        updatedDto!.Language.Should().Be("en");
        updatedDto.Theme.Should().Be("dark");
        updatedDto.Timezone.Should().Be("Asia/Bangkok");

        // 3. Subsequent get confirms persistence in PostgreSQL
        var secondGet = await client.GetAsync("/api/v1/users/me/preferences");
        secondGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var persistedDto = await secondGet.Content.ReadFromJsonAsync<UserPreferenceDto>();
        persistedDto.Should().NotBeNull();
        persistedDto!.Language.Should().Be("en");
        persistedDto.Theme.Should().Be("dark");
    }

    [Fact]
    public async Task SupportTicket_Create_And_Idor_Protection_Returns_404_For_Other_User()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var clientA = _factory.CreateAuthenticatedClient(role: "Member", userId: userA);
        var clientB = _factory.CreateAuthenticatedClient(role: "Member", userId: userB);

        // User A creates a support ticket
        var createReq = new CreateSupportTicketRequest(
            Subject: "Lỗi kết nối ứng dụng",
            Category: "technical",
            Priority: "Normal",
            Message: "Tôi gặp vấn đề khi tải nhật ký tập luyện.",
            AttachmentUrls: null);

        var createResponse = await clientA.PostAsJsonAsync("/api/v1/support/tickets", createReq);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdTicket = await createResponse.Content.ReadFromJsonAsync<SupportTicketDetailDto>();
        createdTicket.Should().NotBeNull();
        var ticketId = createdTicket!.Id;

        // User A can view their own ticket
        var getByAResponse = await clientA.GetAsync($"/api/v1/support/tickets/{ticketId}");
        getByAResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // IDOR Security check: User B tries to view User A's ticket -> MUST return 404 Not Found
        var getByBResponse = await clientB.GetAsync($"/api/v1/support/tickets/{ticketId}");
        getByBResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
