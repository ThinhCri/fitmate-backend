using System.Text.Json;
using Fitmate.Api.Chat;
using Fitmate.Application.Chat;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class ChatContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void ChatMessageDto_Serializes_WithExactExpectedSnakeCaseKeys()
    {
        var msg = new ChatMessageDto(
            Id: Guid.NewGuid(),
            ConversationId: Guid.NewGuid(),
            SenderUserId: Guid.NewGuid(),
            Sender: "user",
            Text: "Hello there!",
            CreatedAt: DateTime.UtcNow,
            ClientMessageId: Guid.NewGuid());

        var json = JsonSerializer.Serialize(msg);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "conversation_id",
            "sender_user_id",
            "sender",
            "text",
            "created_at",
            "client_message_id"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        root.TryGetProperty("conversationId", out _).Should().BeFalse();
        root.TryGetProperty("senderUserId", out _).Should().BeFalse();
        root.TryGetProperty("clientMessageId", out _).Should().BeFalse();
        root.TryGetProperty("createdAt", out _).Should().BeFalse();
    }

    [Fact]
    public void ConversationDto_Serializes_WithExactExpectedSnakeCaseKeys()
    {
        var peerId = Guid.NewGuid();
        var conv = new ConversationDto(
            ConversationId: Guid.NewGuid(),
            Peer: new FriendSummaryDto(peerId, "John Doe", "https://example.com/avatar.jpg"),
            LastMessage: new ChatMessageDto(
                Id: Guid.NewGuid(),
                ConversationId: Guid.NewGuid(),
                SenderUserId: peerId,
                Sender: "peer",
                Text: "See you tomorrow",
                CreatedAt: DateTime.UtcNow,
                ClientMessageId: null),
            UnreadCount: 2,
            LastMessageAt: DateTime.UtcNow);

        var json = JsonSerializer.Serialize(conv);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "conversation_id",
            "peer",
            "last_message",
            "unread_count",
            "last_message_at"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);

        var peer = root.GetProperty("peer");
        peer.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "user_id", "display_name", "avatar_url" });
        peer.GetProperty("user_id").GetGuid().Should().Be(peerId);
        peer.GetProperty("display_name").GetString().Should().Be("John Doe");

        root.TryGetProperty("conversationId", out _).Should().BeFalse();
        root.TryGetProperty("lastMessage", out _).Should().BeFalse();
        root.TryGetProperty("unreadCount", out _).Should().BeFalse();
        root.TryGetProperty("lastMessageAt", out _).Should().BeFalse();
    }

    [Fact]
    public void SendMessageBody_Deserializes_FromSnakeCaseJson()
    {
        var peerId = Guid.NewGuid();
        var clientMsgId = Guid.NewGuid();
        var json = $$"""
        {
            "peer_user_id": "{{peerId}}",
            "text": "Hey coach, what time?",
            "client_message_id": "{{clientMsgId}}"
        }
        """;

        var body = JsonSerializer.Deserialize<SendMessageBody>(json);
        body.Should().NotBeNull();
        body!.PeerUserId.Should().Be(peerId);
        body.Text.Should().Be("Hey coach, what time?");
        body.ClientMessageId.Should().Be(clientMsgId);
    }

    [Fact]
    public void MarkReadBody_Deserializes_FromSnakeCaseJson()
    {
        var msgId = Guid.NewGuid();
        var json = $$"""
        {
            "last_read_message_id": "{{msgId}}"
        }
        """;

        var body = JsonSerializer.Deserialize<MarkReadBody>(json);
        body.Should().NotBeNull();
        body!.LastReadMessageId.Should().Be(msgId);
    }

    [Fact]
    public void ConversationPage_Serializes_WithPaginationMetadata()
    {
        var page = new ConversationPage(
            Items: new List<ConversationDto>(),
            NextCursor: "cursor-token-123");

        var json = JsonSerializer.Serialize(page);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "items", "nextCursor" });
    }

    [Fact]
    public void MessagesPage_Serializes_WithPaginationMetadata()
    {
        var page = new MessagesPage(
            Items: new List<ChatMessageDto>(),
            NextCursor: "msg-cursor-456");

        var json = JsonSerializer.Serialize(page);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "items", "nextCursor" });
    }

    [Fact]
    public void OpenApi_ChatSchemas_Match_RuntimeContract()
    {
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);

        diagnostic.Errors.Should().BeEmpty();

        // 1. ChatMessage schema
        doc.Components.Schemas.Should().ContainKey("ChatMessage");
        var chatMsgSchema = doc.Components.Schemas["ChatMessage"];
        var expectedChatMsgProps = new[] { "id", "conversation_id", "sender_user_id", "sender", "text", "client_message_id", "created_at" };
        chatMsgSchema.Properties.Keys.Should().BeEquivalentTo(expectedChatMsgProps);

        // 2. ChatPeer schema
        doc.Components.Schemas.Should().ContainKey("ChatPeer");
        var chatPeerSchema = doc.Components.Schemas["ChatPeer"];
        chatPeerSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "user_id", "display_name", "avatar_url" });

        // 3. Conversation schema
        doc.Components.Schemas.Should().ContainKey("Conversation");
        var convSchema = doc.Components.Schemas["Conversation"];
        var expectedConvProps = new[] { "conversation_id", "peer", "last_message", "unread_count", "last_message_at" };
        convSchema.Properties.Keys.Should().BeEquivalentTo(expectedConvProps);

        // 4. MarkReadRequest schema
        doc.Components.Schemas.Should().ContainKey("MarkReadRequest");
        var markReadSchema = doc.Components.Schemas["MarkReadRequest"];
        markReadSchema.Properties.Keys.Should().BeEquivalentTo(new[] { "last_read_message_id" });
    }
}
