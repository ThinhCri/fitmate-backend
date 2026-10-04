using Fitmate.Domain.Chat;

namespace Fitmate.Application.Chat;

public interface IAiChatStore
{
    Task<IReadOnlyList<AiThreadSummaryRow>> ListThreadsAsync(Guid userId, CancellationToken ct);
    Task<ChatThread?> FindThreadAsync(Guid threadId, CancellationToken ct);
    Task AddThreadAsync(ChatThread thread, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid threadId, int limit, CancellationToken ct);
    Task AddMessageAsync(ChatMessage message, CancellationToken ct);
    Task RemoveMessagesAsync(Guid threadId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public sealed record AiThreadSummaryRow(ChatThread Thread, int MessageCount);
