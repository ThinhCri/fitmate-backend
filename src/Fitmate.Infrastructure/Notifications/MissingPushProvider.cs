using Fitmate.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>
/// Push provider used when the backend has no push provider configured.
/// Always returns <see cref="PushSendResult.ProviderNotConfigured"/> so the
/// worker correctly marks deliveries as permanently failed without retrying.
/// </summary>
public sealed class MissingPushProvider : IPushProvider
{
    private readonly ILogger<MissingPushProvider> _logger;

    public MissingPushProvider(ILogger<MissingPushProvider> logger) => _logger = logger;

    public Task<PushSendResult> SendAsync(
        string decryptedToken,
        string title,
        string? summary,
        string payloadJson,
        CancellationToken ct)
    {
        // Log which notification would have been sent — no token, no title content.
        _logger.LogWarning(
            "MissingPushProvider: push send attempted with no provider configured. " +
            "Title: {Title}, HasPayload: {HasPayload}",
            title,
            !string.IsNullOrEmpty(payloadJson));

        return Task.FromResult(
            PushSendResult.ProviderNotConfigured("push_provider_not_configured"));
    }
}
