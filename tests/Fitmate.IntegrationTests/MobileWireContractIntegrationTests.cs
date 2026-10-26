using System.Net;
using System.Text.Json;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

/// <summary>
/// Exercises the public mobile wire contract through the real ASP.NET Core pipeline.
/// These tests deliberately inspect raw JSON so DTO deserialization cannot hide a
/// casing or response-envelope regression.
/// </summary>
public sealed class MobileWireContractIntegrationTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public MobileWireContractIntegrationTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task Authenticated_EmptyCollections_UseCanonicalHttpEnvelopes()
    {
        var member = _factory.CreateAuthenticatedClient("Member", Guid.NewGuid());
        var pt = _factory.CreateAuthenticatedClient("PT", Guid.NewGuid());

        await AssertObjectKeys(member, "/api/v1/me/workout/plans", "items");
        await AssertJsonKind(member, "/api/v1/me/nutrition/meals", JsonValueKind.Array);
        await AssertObjectKeys(member, "/api/v1/social/posts", "items", "nextCursor");
        await AssertObjectKeys(member, "/api/v1/chat/friends/conversations", "items", "nextCursor");
        await AssertObjectKeys(pt, "/api/v1/me/revenue/history", "items", "total", "page", "pageSize");
        await AssertObjectKeys(pt, "/api/v1/me/revenue", "range", "total_gross_vnd",
            "platform_fee_vnd", "net_earnings_vnd", "pending_payout_vnd",
            "completed_payout_vnd", "completed_sessions_count", "chart", "refund_adjustment_vnd");
    }

    [Fact]
    public async Task SocialFeed_SearchesContentAuthorAndHashtag_OnPostgres()
    {
        var authorId = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient("Member", authorId);
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var contentToken = $"search-content-{suffix}";
        var authorToken = $"Search Author {suffix}";
        var tagToken = $"searchtag{suffix}";
        Guid postId;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var user = await db.Users.FindAsync(authorId);
            user.Should().NotBeNull();
            user!.FullName = authorToken;
            var post = Post.Create(authorId, contentToken, null, null, [tagToken], PostVisibility.Public, false);
            postId = post.Id;
            db.Posts.Add(post);
            await db.SaveChangesAsync();
        }

        await AssertFeedContains(client, $"/api/v1/social/posts?query={Uri.EscapeDataString(contentToken)}", postId);
        await AssertFeedContains(client, $"/api/v1/social/posts?query={Uri.EscapeDataString(authorToken)}", postId);
        await AssertFeedContains(client, $"/api/v1/social/posts?query={Uri.EscapeDataString($"#{tagToken}")}", postId);
    }

    [Fact]
    public async Task UserSearch_AndSuggestions_ReturnEligibleOtherAccounts()
    {
        var viewerId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var existingFriendId = Guid.NewGuid();
        var pendingRequestId = Guid.NewGuid();
        var blockedUserId = Guid.NewGuid();
        var viewer = _factory.CreateAuthenticatedClient("Member", viewerId);
        _factory.CreateAuthenticatedClient("Member", candidateId).Dispose();
        _factory.CreateAuthenticatedClient("Member", existingFriendId).Dispose();
        _factory.CreateAuthenticatedClient("Member", pendingRequestId).Dispose();
        _factory.CreateAuthenticatedClient("Member", blockedUserId).Dispose();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var candidate = await db.Users.FindAsync(candidateId);
            candidate.Should().NotBeNull();
            candidate!.FullName = $"Suggested Friend {suffix}";
            db.Friendships.Add(Friendship.Create(viewerId, existingFriendId));
            db.FriendRequests.Add(FriendRequest.Create(viewerId, pendingRequestId));
            db.BlockedUsers.Add(BlockedUser.Create(blockedUserId, viewerId));
            await db.SaveChangesAsync();
        }

        using var searchResponse = await viewer.GetAsync($"/api/v1/users/search?q={suffix}");
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        searchJson.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(candidateId).And.NotContain(viewerId);

        using var suggestionsResponse = await viewer.GetAsync("/api/v1/users/suggestions?limit=50");
        suggestionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var suggestionsJson = JsonDocument.Parse(await suggestionsResponse.Content.ReadAsStringAsync());
        var suggestedIds = suggestionsJson.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();
        suggestedIds.Should().Contain(candidateId);
        suggestedIds.Should().NotContain(new[] { viewerId, existingFriendId, pendingRequestId, blockedUserId });
    }

    [Fact]
    public async Task AppointmentDetail_UsesCanonicalWireShape_AndOwnerBoundary()
    {
        var memberId = Guid.NewGuid();
        var ptId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var member = _factory.CreateAuthenticatedClient("Member", memberId);
        _factory.CreateAuthenticatedClient("PT", ptId).Dispose();
        var outsider = _factory.CreateAuthenticatedClient("Member", outsiderId);

        var appointmentId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var service = Service.Create(ptId, "Wire contract service", "Integration test", 100_000, 60);
            var appointment = Appointment.CreateHold(
                memberId,
                ptId,
                service.Id,
                DateTime.UtcNow.AddDays(2),
                DateTime.SpecifyKind(DateTime.UtcNow.AddDays(2).AddHours(7), DateTimeKind.Unspecified),
                60,
                100_000);
            appointmentId = appointment.Id;
            db.Services.Add(service);
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
        }

        using var response = await member.GetAsync($"/api/v1/me/bookings/{appointmentId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        keys.Should().Contain(new[]
        {
            "id", "member_id", "pt_user_id", "service_id", "slot_start_utc",
            "duration_minutes", "status", "platform_fee_rate_bps"
        });
        keys.Should().NotContain("slot_start_local");
        keys.Should().NotContain("note");
        keys.Should().NotContain("created_at");

        using var forbidden = await outsider.GetAsync($"/api/v1/me/bookings/{appointmentId}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = await _factory.CreateClient().GetAsync($"/api/v1/me/bookings/{appointmentId}");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task AssertObjectKeys(HttpClient client, string path, params string[] expectedKeys)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        var keys = json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        keys.Should().BeEquivalentTo(expectedKeys, path);
    }

    private static async Task AssertJsonKind(HttpClient client, string path, JsonValueKind expectedKind)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.ValueKind.Should().Be(expectedKind);
    }

    private static async Task AssertFeedContains(HttpClient client, string path, Guid postId)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(postId);
    }
}
