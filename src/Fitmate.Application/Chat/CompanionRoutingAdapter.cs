using Fitmate.Domain.Common;

namespace Fitmate.Application.Chat;

/// <summary>Keep text on the chosen provider; send images only to the configured vision adapter.</summary>
public sealed class CompanionRoutingAdapter(IAiCompanionAdapter text, IAiCompanionAdapter? vision) : IAiCompanionAdapter
{
    public Task<CompanionChatResponseView> GenerateReplyAsync(CompanionChatRequest request, CancellationToken ct) =>
        request.Images?.Count > 0
            ? vision?.GenerateReplyAsync(request, ct) ?? throw new AiUnavailableException("Image chat requires the configured Gemini vision provider.")
            : text.GenerateReplyAsync(request, ct);
}
