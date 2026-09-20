using Fitmate.Application.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Abstraction over the actual push provider (FCM, Expo, ...).
///
/// <para>
/// One concrete implementation is registered via DI based on
/// <c>PushProviderConfig</c>. A "missing provider" implementation is used when
/// the provider is not configured — it always returns
/// <see cref="PushSendResult.ProviderNotConfigured"/> so the worker correctly
/// marks the delivery as permanently failed without retrying.
/// </para>
///
/// <para>
/// Fake / test-only providers MUST NOT be registered in production DI.
/// </para>
/// </summary>
public interface IPushProvider
{
    /// <summary>
    /// Send a push notification to a single device token.
    /// </summary>
    /// <param name="decryptedToken">Raw token value (already decrypted by the caller).</param>
    /// <param name="title">Push title.</param>
    /// <param name="summary">Optional short lock-screen text (&lt;=80 chars recommended).</param>
    /// <param name="payloadJson">Notification payload as JSON string.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PushSendResult> SendAsync(
        string decryptedToken,
        string title,
        string? summary,
        string payloadJson,
        CancellationToken ct = default);
}
