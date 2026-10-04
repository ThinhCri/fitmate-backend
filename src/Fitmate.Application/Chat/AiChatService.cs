using System.Text.Json.Serialization;
using Fitmate.Domain.Chat;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Chat;

public sealed record AiThreadView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("last_message_at")] DateTime LastMessageAt,
    [property: JsonPropertyName("message_count")] int MessageCount);

public sealed record AiMessageView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("conversation_id")] Guid? ConversationId,
    [property: JsonPropertyName("sender_user_id")] Guid SenderUserId,
    [property: JsonPropertyName("sender")] string Sender,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("client_message_id")] Guid? ClientMessageId,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public interface IAiChatService
{
    Task<IReadOnlyList<AiThreadView>> ListThreadsAsync(Guid userId, CancellationToken ct);
    Task<AiThreadView> CreateThreadAsync(Guid userId, string? title, CancellationToken ct);
    Task<AiThreadView> RenameThreadAsync(Guid userId, Guid threadId, string title, CancellationToken ct);
    Task DeleteThreadAsync(Guid userId, Guid threadId, CancellationToken ct);
    Task<IReadOnlyList<AiMessageView>> ListMessagesAsync(Guid userId, Guid threadId, int limit, CancellationToken ct);
    Task<AiMessageView> SendAsync(Guid userId, Guid threadId, string text, byte[]? imageData, string? imageMimeType, CancellationToken ct);
}

public sealed class AiChatService : IAiChatService
{
    private readonly IAiChatStore _store;
    private readonly IAiCompanionAdapter _adapter;

    public AiChatService(IAiChatStore store, IAiCompanionAdapter adapter)
    {
        _store = store;
        _adapter = adapter;
    }

    public async Task<IReadOnlyList<AiThreadView>> ListThreadsAsync(Guid userId, CancellationToken ct)
        => (await _store.ListThreadsAsync(userId, ct))
            .Select(x => MapThread(x.Thread, x.MessageCount)).ToList();

    public async Task<AiThreadView> CreateThreadAsync(Guid userId, string? title, CancellationToken ct)
    {
        var normalized = string.IsNullOrWhiteSpace(title) ? "Cuộc trò chuyện mới" : title.Trim();
        if (normalized.Length > 200)
            throw Invalid("title", "Tiêu đề không được dài quá 200 ký tự.");
        var thread = ChatThread.CreateAi(userId, normalized);
        await _store.AddThreadAsync(thread, ct);
        await _store.SaveChangesAsync(ct);
        return MapThread(thread, 0);
    }

    public async Task<AiThreadView> RenameThreadAsync(
        Guid userId, Guid threadId, string title, CancellationToken ct)
    {
        var normalized = title?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 200)
            throw Invalid("title", "Tiêu đề phải có độ dài từ 1 đến 200 ký tự.");

        var thread = await RequireOwnedThread(userId, threadId, ct);
        thread.Rename(normalized);
        await _store.SaveChangesAsync(ct);
        var messages = await _store.ListMessagesAsync(threadId, 100, ct);
        return MapThread(thread, messages.Count);
    }

    public async Task DeleteThreadAsync(Guid userId, Guid threadId, CancellationToken ct)
    {
        var thread = await RequireOwnedThread(userId, threadId, ct);
        await _store.RemoveMessagesAsync(threadId, ct);
        thread.SoftDelete();
        await _store.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AiMessageView>> ListMessagesAsync(
        Guid userId, Guid threadId, int limit, CancellationToken ct)
    {
        await RequireOwnedThread(userId, threadId, ct);
        limit = Math.Clamp(limit, 1, 100);
        return (await _store.ListMessagesAsync(threadId, limit, ct)).Select(MapMessage).ToList();
    }

    public async Task<AiMessageView> SendAsync(
        Guid userId, Guid threadId, string text, byte[]? imageData, string? imageMimeType, CancellationToken ct)
    {
        var thread = await RequireOwnedThread(userId, threadId, ct);
        var normalized = text?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2000)
            throw Invalid("text", "Tin nhắn phải có độ dài từ 1 đến 2000 ký tự.");

        var historyRows = await _store.ListMessagesAsync(threadId, 40, ct);
        var history = historyRows.Select(m => new CompanionChatMessageView(
            m.Sender == Fitmate.Domain.Chat.ChatMessageSender.Ai ? "model" : "user", m.Text, m.CreatedAt)).ToList();
        var response = await _adapter.GenerateReplyAsync(
            new CompanionChatRequest(userId, threadId, normalized, history, imageData, imageMimeType), ct);

        var userMessage = ChatMessage.CreateUser(threadId, userId, normalized);
        var aiMessage = ChatMessage.CreateAi(threadId, response.ReplyText, null);
        await _store.AddMessageAsync(userMessage, ct);
        await _store.AddMessageAsync(aiMessage, ct);
        thread.TouchLastMessage();
        await _store.SaveChangesAsync(ct);
        return MapMessage(aiMessage);
    }

    private async Task<ChatThread> RequireOwnedThread(Guid userId, Guid threadId, CancellationToken ct)
    {
        var thread = await _store.FindThreadAsync(threadId, ct);
        if (thread is null || thread.MemberId != userId)
            throw new NotFoundException(ErrorCodes.Chat.ConversationNotFound, "Không tìm thấy cuộc trò chuyện.");
        return thread;
    }

    private static AiThreadView MapThread(ChatThread x, int count) =>
        new(x.Id, x.Title ?? "Cuộc trò chuyện AI", x.LastMessageAt, count);

    private static AiMessageView MapMessage(ChatMessage x) => new(
        x.Id, x.ConversationId, x.SenderUserId ?? Guid.Empty,
        x.Sender == Fitmate.Domain.Chat.ChatMessageSender.Ai ? "ai" : "user", x.Text, x.ClientMessageId, x.CreatedAt);

    private static ValidationException Invalid(string field, string message) => new(
        ErrorCodes.Validation.FieldInvalid, message,
        new Dictionary<string, string[]> { [field] = new[] { message } });
}
