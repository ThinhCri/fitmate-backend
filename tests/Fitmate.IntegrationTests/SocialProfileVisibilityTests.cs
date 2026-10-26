using System.Net;
using System.Text.Json;
using Fitmate.Domain.Social;
using Fitmate.Domain.Common;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class SocialProfileVisibilityTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory factory;
    public SocialProfileVisibilityTests(FitmateTestAppFactory factory)
    {
        this.factory = factory;
        factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task AuthorFeed_PaginatesVisiblePosts_ProtectsPrivateAndBlockedPosts()
    {
        var viewerId = Guid.NewGuid(); var authorId = Guid.NewGuid(); var otherId = Guid.NewGuid();
        using var viewer = factory.CreateAuthenticatedClient("Member", viewerId);
        using var author = factory.CreateAuthenticatedClient("Member", authorId);
        using var other = factory.CreateAuthenticatedClient("Member", otherId);
        var expected = new HashSet<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            db.Friendships.Add(Friendship.Create(viewerId, authorId));
            foreach (var visibility in new[] { PostVisibility.Public, PostVisibility.Public, PostVisibility.Friends })
            {
                var post = Post.Create(authorId, "visible", null, null, [], visibility, false);
                expected.Add(post.Id); db.Posts.Add(post);
            }
            db.Posts.Add(Post.Create(authorId, "private", null, null, [], PostVisibility.Private, false));
            db.Posts.Add(Post.Create(otherId, "other author", null, null, [], PostVisibility.Public, false));
            await db.SaveChangesAsync();
        }
        var ids = new List<Guid>(); string? cursor = null;
        do
        {
            using var response = await viewer.GetAsync($"/api/v1/social/posts?scope=friends&author_id={authorId}&limit=2&cursor={Uri.EscapeDataString(cursor ?? "")}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            ids.AddRange(json.RootElement.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));
            cursor = json.RootElement.GetProperty("nextCursor").GetString();
            ids.Count.Should().BeLessThanOrEqualTo(3);
        } while (cursor is not null);
        ids.Should().BeEquivalentTo(expected);
        using var strangerResponse = await other.GetAsync($"/api/v1/social/posts?scope=friends&author_id={authorId}");
        using var strangerJson = JsonDocument.Parse(await strangerResponse.Content.ReadAsStringAsync());
        strangerJson.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            db.BlockedUsers.Add(BlockedUser.Create(authorId, viewerId)); await db.SaveChangesAsync();
        }
        using var blocked = await viewer.GetAsync($"/api/v1/social/posts?scope=friends&author_id={authorId}");
        using var blockedJson = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        blockedJson.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AuthorFeed_RejectsInvalidFilter(string authorId)
    {
        using var viewer = factory.CreateAuthenticatedClient("Member", Guid.NewGuid());
        using var response = await viewer.GetAsync($"/api/v1/social/posts?author_id={authorId}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuthorFeed_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/social/posts?author_id={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
