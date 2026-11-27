using System.Net;
using System.Net.Http.Json;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
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

    [Fact]
    public async Task PtAvatarAttachment_UpdatesPublicMarketplaceAuthLoginAndSocialResponses()
    {
        var ptId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        _factory.CreateAuthenticatedClient("PT", ptId).Dispose();
        var uniqueSpecialty = $"AvatarSpec{ptId:N}"[..18];
        var ptEmail = $"pt_{ptId:N}@fitmate.vn";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(ptId.ToString());
            user.Should().NotBeNull();
            (await users.AddPasswordAsync(user!, "PtPass!12345")).Succeeded.Should().BeTrue();

            var profile = PtProfile.CreateDraft(ptId, uniqueSpecialty, 4, 350_000, "Avatar test PT");
            profile.Approve(adminId);
            var cert = Certificate.Submit(ptId, "CPT", "NASM", $"CODE-{ptId:N}"[..12],
                new DateOnly(2025, 1, 1), "certs/test-cert.jpg", ptId);
            cert.Approve(adminId);
            var service = Service.Create(ptId, "PT Avatar Session", "Session description", 350_000, 60);
            db.AddRange(profile, cert, service);
            await db.SaveChangesAsync();
        }

        using var ptClient = _factory.CreateAuthenticatedClient("PT", ptId);

        var firstKey = $"avatars/{ptId:N}_avatar1.jpg";
        var secondKey = $"avatars/{ptId:N}_avatar2.jpg";

        using (var attach1 = await ptClient.PutAsJsonAsync("/api/v1/users/me/avatar", new { storage_key = firstKey }))
        {
            attach1.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var anon = _factory.CreateClient();
        using (var listRes = await anon.GetAsync($"/api/v1/trainers?specialty={Uri.EscapeDataString(uniqueSpecialty)}"))
        {
            listRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await listRes.Content.ReadAsStringAsync());
            var item = doc.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ptId);
            item.GetProperty("avatar_url").GetString().Should().Contain(firstKey);
        }

        using (var detailRes = await anon.GetAsync($"/api/v1/trainers/{ptId}"))
        {
            detailRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await detailRes.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("avatar_url").GetString().Should().Contain(firstKey);
        }

        using (var loginRes = await anon.PostAsJsonAsync("/api/v1/auth/login", new { email = ptEmail, password = "PtPass!12345" }))
        {
            loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("user").GetProperty("avatar_url").GetString().Should().Contain(firstKey);
        }

        using (var postRes = await ptClient.PostAsJsonAsync("/api/v1/social/posts", new { content = $"Avatar post {ptId:N}", visibility = "public" }))
        {
            postRes.StatusCode.Should().Be(HttpStatusCode.Created);
            using var doc = JsonDocument.Parse(await postRes.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("author").GetProperty("avatar_url").GetString().Should().Contain(firstKey);
        }

        using (var attach2 = await ptClient.PutAsJsonAsync("/api/v1/users/me/avatar", new { storage_key = secondKey }))
        {
            attach2.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var detailRes2 = await anon.GetAsync($"/api/v1/trainers/{ptId}"))
        {
            detailRes2.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await detailRes2.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("avatar_url").GetString().Should().Contain(secondKey);
        }
    }

    [Fact]
    public async Task PublicTrainerDetail_IncludesSubmittedReviews_ForOtherUsers()
    {
        var ptId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var reviewerClient = _factory.CreateAuthenticatedClient("Member", reviewerId);
        var otherMemberClient = _factory.CreateAuthenticatedClient("Member", otherUserId);
        _factory.CreateAuthenticatedClient("PT", ptId).Dispose();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var reviewer = await db.Users.FindAsync(reviewerId);
            reviewer.Should().NotBeNull();
            reviewer!.FullName = "Học viên Minh Anh";

            var profile = PtProfile.CreateDraft(ptId, "Tăng cơ", 5, 300_000, "HLV review test");
            profile.Approve(adminId);
            var cert = Certificate.Submit(ptId, "CPT", "ACE", $"REV-{ptId:N}"[..12],
                new DateOnly(2025, 1, 1), "certs/rev-cert.jpg", ptId);
            cert.Approve(adminId);
            var service = Service.Create(ptId, "Buổi tập review", "Test session", 300_000, 60);

            var startUtc = DateTime.UtcNow.AddDays(-1);
            var appt = Appointment.CreateHold(
                reviewerId,
                ptId,
                service.Id,
                startUtc,
                DateTime.SpecifyKind(startUtc.AddHours(7), DateTimeKind.Unspecified),
                60,
                300_000);
            appt.ForceStatusForTesting(AppointmentStatus.Completed);

            db.AddRange(profile, cert, service, appt);
            await db.SaveChangesAsync();
        }

        using (var submitRes = await reviewerClient.PostAsJsonAsync(
                   $"/api/v1/trainers/{ptId}/reviews",
                   new { rating = 5, text = "HLV hướng dẫn rất chi tiết và nhiệt tình!" }))
        {
            submitRes.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using (var otherDetailRes = await otherMemberClient.GetAsync($"/api/v1/trainers/{ptId}"))
        {
            otherDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await otherDetailRes.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("reviews_count").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("rating").GetDecimal().Should().Be(5m);
            var reviews = doc.RootElement.GetProperty("reviews").EnumerateArray().ToList();
            reviews.Should().ContainSingle();
            reviews[0].GetProperty("reviewer_name").GetString().Should().Be("Học viên Minh Anh");
            reviews[0].GetProperty("rating").GetInt32().Should().Be(5);
            reviews[0].GetProperty("text").GetString().Should().Be("HLV hướng dẫn rất chi tiết và nhiệt tình!");
        }

        using (var otherReviewsRes = await _factory.CreateClient().GetAsync($"/api/v1/trainers/{ptId}/reviews"))
        {
            otherReviewsRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var doc = JsonDocument.Parse(await otherReviewsRes.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("total_reviews").GetInt32().Should().Be(1);
            doc.RootElement.GetProperty("reviews").EnumerateArray().Should().ContainSingle();
        }
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
