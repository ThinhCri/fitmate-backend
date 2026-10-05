using System.Text.Json;
using Fitmate.Api.Social;
using Fitmate.Application.Social;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class SocialContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void PostView_Serializes_WithExactExpectedKeys_AndNoCamelCase()
    {
        var authorId = Guid.NewGuid();
        var post = new PostView(
            Id: Guid.NewGuid(),
            AuthorId: authorId,
            Author: new PostAuthor(authorId, "Coach Alex", "https://example.com/avatar.jpg"),
            Content: "Great workout today!",
            ImageUrl: "https://example.com/post.jpg",
            ImageUrls: new[] { "https://example.com/post.jpg" },
            Link: null,
            Tags: new List<string> { "fitness", "motivation" },
            Visibility: "public",
            IsKnowledge: true,
            ReactionsCount: 15,
            CommentsCount: 3,
            CreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(post);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "author_id",
            "author",
            "content",
            "image_url",
            "image_urls",
            "link",
            "tags",
            "visibility",
            "is_knowledge",
            "reactions_count",
            "comments_count",
            "created_at",
            "saved_by_viewer",
            "reacted_by_viewer",
            "image_storage_keys"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);

        var author = root.GetProperty("author");
        author.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "id", "display_name", "avatar_url" });
        author.GetProperty("display_name").GetString().Should().Be("Coach Alex");
    }

    [Fact]
    public void CommentView_Serializes_WithExactExpectedKeys()
    {
        var authorId = Guid.NewGuid();
        var comment = new CommentView(
            Id: Guid.NewGuid(),
            PostId: Guid.NewGuid(),
            AuthorId: authorId,
            Author: new PostAuthor(authorId, "Member Jane", null),
            Text: "Inspiring work!",
            CreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(comment);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "post_id",
            "author_id",
            "parent_comment_id",
            "author",
            "text",
            "created_at",
            "likes_count",
            "liked_by_viewer"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void FriendRequestView_Serializes_WithExactExpectedKeys()
    {
        var req = new FriendRequestView(
            Id: Guid.NewGuid(),
            SenderId: Guid.NewGuid(),
            RecipientId: Guid.NewGuid(),
            Status: "pending",
            CreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(req);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "id", "sender_id", "recipient_id", "status", "created_at" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void FriendSummary_Serializes_WithExactExpectedKeys()
    {
        var friend = new FriendSummary(
            Id: Guid.NewGuid(),
            DisplayName: "John Doe",
            AvatarUrl: null,
            FriendsSince: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(friend);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "id", "display_name", "avatar_url", "friends_since" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void FriendRequestStatusView_Serializes_WithExactExpectedKeys()
    {
        var status = new FriendRequestStatusView(
            Relation: "outgoing_pending",
            RequestId: Guid.NewGuid(),
            RequestCreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(status);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "relation", "request_id", "request_created_at" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void BlockedSummary_Serializes_WithExactExpectedKeys()
    {
        var blocked = new BlockedSummary(Guid.NewGuid(), DateTime.UtcNow);
        var json = JsonSerializer.Serialize(blocked);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "id", "blocked_at" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void SocialRequests_Deserialize_FromSnakeCase()
    {
        var postInputJson = """
        {
            "content": "Running 5k",
            "image_storage_key": "posts/run.jpg",
            "link": null,
            "tags": ["running"],
            "visibility": "public",
            "is_knowledge": false
        }
        """;
        var postInput = JsonSerializer.Deserialize<PostInputDto>(postInputJson);
        postInput.Should().NotBeNull();
        postInput!.ImageStorageKey.Should().Be("posts/run.jpg");
        postInput.IsKnowledge.Should().BeFalse();

        var updatePostJson = """
        {
            "content": "Updated run",
            "image_storage_keys": ["posts/owner_updated.jpg"],
            "tags": ["running", "workout_exp"],
            "is_knowledge": true
        }
        """;
        var updatePost = JsonSerializer.Deserialize<UpdatePostBody>(updatePostJson);
        updatePost.Should().NotBeNull();
        updatePost!.ImageStorageKeys.Should().Equal("posts/owner_updated.jpg");
        updatePost.Tags.Should().Equal("running", "workout_exp");
        updatePost.IsKnowledge.Should().BeTrue();

        var sendFriendJson = """{ "to_user_id": "11111111-1111-1111-1111-111111111111" }""";
        var sendFriend = JsonSerializer.Deserialize<SendFriendRequestBody>(sendFriendJson);
        sendFriend.Should().NotBeNull();
        sendFriend!.ToUserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        var blockJson = """{ "user_id": "22222222-2222-2222-2222-222222222222" }""";
        var block = JsonSerializer.Deserialize<BlockBody>(blockJson);
        block.Should().NotBeNull();
        block!.UserId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var reportJson = """
        {
            "target_type": "post",
            "target_id": "33333333-3333-3333-3333-333333333333",
            "reason": "Spam content",
            "details": "Multiple links"
        }
        """;
        var report = JsonSerializer.Deserialize<ReportBody>(reportJson);
        report.Should().NotBeNull();
        report!.TargetType.Should().Be("post");
        report.TargetId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"));
    }

    [Fact]
    public void OpenApi_SocialSchemas_MatchExpectedProperties()
    {
        File.Exists(OpenApiPath).Should().BeTrue();
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);
        diagnostic.Errors.Should().BeEmpty();

        doc.Paths.Should().ContainKey("/social/bookmarks");

        doc.Components.Schemas.Should().ContainKey("Post");
        var postSchema = doc.Components.Schemas["Post"];
        var expectedPostProperties = new HashSet<string>
        {
            "id",
            "author_id",
            "author",
            "content",
            "image_url",
            "image_urls",
            "link",
            "tags",
            "visibility",
            "is_knowledge",
            "reactions_count",
            "comments_count",
            "created_at",
            "saved_by_viewer",
            "reacted_by_viewer",
            "image_storage_keys"
        };
        postSchema.Properties.Keys.Should().BeEquivalentTo(expectedPostProperties);

        doc.Components.Schemas.Should().ContainKey("PostAuthor");
        var authorSchema = doc.Components.Schemas["PostAuthor"];
        authorSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "id", "display_name", "avatar_url" });

        doc.Components.Schemas.Should().ContainKey("Comment");
        var commentSchema = doc.Components.Schemas["Comment"];
        commentSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "id", "post_id", "author_id", "parent_comment_id", "author", "text", "created_at", "likes_count", "liked_by_viewer" });

        doc.Components.Schemas.Should().ContainKey("FriendRequest");
        var friendReqSchema = doc.Components.Schemas["FriendRequest"];
        friendReqSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "id", "sender_id", "recipient_id", "status", "created_at" });

        doc.Components.Schemas.Should().ContainKey("FriendSummary");
        var friendSumSchema = doc.Components.Schemas["FriendSummary"];
        friendSumSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "id", "display_name", "avatar_url", "friends_since" });

        doc.Components.Schemas.Should().ContainKey("FriendRelationStatus");
        var relSchema = doc.Components.Schemas["FriendRelationStatus"];
        relSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "relation", "request_id", "request_created_at" });

        doc.Components.Schemas.Should().ContainKey("BlockedSummary");
        var blockSchema = doc.Components.Schemas["BlockedSummary"];
        blockSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "id", "blocked_at" });
    }
}
