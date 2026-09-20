using System.Text.Json;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

// ─── Query ─────────────────────────────────────────────────────────────────

public sealed record NotificationView(
    Guid Id,
    NotificationType Type,
    string Title,
    string Message,
    string? Summary,
    JsonElement? Payload,
    bool Read,
    DateTime CreatedAt);

public sealed record NotificationPage(
    IReadOnlyList<NotificationView> Items,
    int Total);

// ─── Command ─────────────────────────────────────────────────────────────────

public sealed record RegisterDeviceRequest(
    string DeviceToken,
    string Platform,  // "android" | "ios" | "web"
    string Provider); // "fcm" | "expo"

public sealed record RegisterDeviceResult(
    Guid DeviceId,
    DateTime RegisteredAt);

// ─── Internal (used by dispatcher) ───────────────────────────────────────────

/// <summary>
/// Payload validated by <c>PushPayload.Allowlist</c> before serializing.
/// </summary>
public sealed record NotificationPayload(
    Guid RecipientId,
    NotificationType Type,
    string Title,
    string? Summary,
    object? Payload,
    Guid SourceEventId,
    string SourceType);

/// <summary>
/// Result of a single push send attempt by <see cref="IPushProvider"/>.
/// </summary>
public sealed record PushSendResult(
    PushSendOutcome Outcome,
    string? ProviderMessage)
{
    public static PushSendResult Success(string? providerMessage = null)
        => new(PushSendOutcome.Success, providerMessage);

    public static PushSendResult Retryable(string providerMessage)
        => new(PushSendOutcome.Retryable, providerMessage);

    public static PushSendResult TokenInvalid(string providerMessage)
        => new(PushSendOutcome.TokenInvalid, providerMessage);

    public static PushSendResult Deferred(string providerMessage)
        => new(PushSendOutcome.Deferred, providerMessage);

    /// <summary>
    /// Provider is not configured or disabled. Worker must NOT retry indefinitely.
    /// </summary>
    public static PushSendResult ProviderNotConfigured(string providerMessage)
        => new(PushSendOutcome.ProviderNotConfigured, providerMessage);

    /// <summary>
    /// Payload rejected by the provider (size, field, format).
    /// </summary>
    public static PushSendResult PayloadRejected(string providerMessage)
        => new(PushSendOutcome.PayloadRejected, providerMessage);
}

public enum PushSendOutcome
{
    Success = 0,
    Retryable = 1,
    TokenInvalid = 2,
    Deferred = 3,
    ProviderNotConfigured = 4,
    PayloadRejected = 5,
}
