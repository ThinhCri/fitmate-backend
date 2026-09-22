using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Chat;

/// <summary>
/// BE-817 PREPARE NOW: AI Companion chat adapter stub.
/// Validates input, cancellation, and config gating; throws typed 503 AiUnavailableException.
/// </summary>
public class AiCompanionStubAdapter : IAiCompanionAdapter
{
    private readonly IOptions<AiOptions>? _options;

    public AiCompanionStubAdapter(IOptions<AiOptions>? options = null)
    {
        _options = options;
    }

    public Task<CompanionChatResponseView> GenerateReplyAsync(
        CompanionChatRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (request.UserId == Guid.Empty)
        {
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "UserId is required for companion chat.",
                new Dictionary<string, string[]>
                {
                    ["userId"] = new[] { "UserId must be a valid non-empty GUID." }
                });
        }

        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            throw new ValidationException(
                ErrorCodes.Validation.RequiredField,
                "Message text cannot be empty.",
                new Dictionary<string, string[]>
                {
                    ["userMessage"] = new[] { "Message content cannot be whitespace or empty." }
                });
        }

        if (request.UserMessage.Length > 2000)
        {
            throw new ValidationException(
                ErrorCodes.Validation.TooLong,
                "Message text exceeds maximum length of 2000 characters.",
                new Dictionary<string, string[]>
                {
                    ["userMessage"] = new[] { "Max length is 2000 characters." }
                });
        }

        var opt = _options?.Value;
        if (opt is not null && opt.Enabled && opt.CompanionChatEnabled)
        {
            throw new AiUnavailableException("AI companion chat model artifact is not yet integrated.");
        }

        throw new AiUnavailableException("AI companion chat is currently disabled or unavailable awaiting model handoff.");
    }
}

