namespace Fitmate.Application.Chat;

public sealed record CompanionChatMessageView(
    string Role,
    string Content,
    DateTime CreatedAt);

public sealed record CompanionChatRequest(
    Guid UserId,
    Guid ThreadId,
    string UserMessage,
    IReadOnlyList<CompanionChatMessageView>? History = null,
    IReadOnlyList<CompanionImageInput>? Images = null);

public sealed record CompanionImageInput(byte[] Data, string MimeType);

public sealed record CompanionChatResponseView(
    Guid MessageId,
    string ReplyText,
    DateTime CreatedAt,
    string Source,
    string? Disclaimer);

/// <summary>
/// BE-817 / ai-contract.md §2 & §5: Boundary for AI Companion chat.
/// Inference boundary with owner thread/history isolation.
/// Strictly non-authoritative: cannot grant roles, verify certificates, or execute commands.
/// </summary>
public interface IAiCompanionAdapter
{
    Task<CompanionChatResponseView> GenerateReplyAsync(
        CompanionChatRequest request,
        CancellationToken ct);
}

