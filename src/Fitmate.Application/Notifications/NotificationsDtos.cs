using System.Text.Json;
using System.Text.Json.Serialization;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

// ─── Query ─────────────────────────────────────────────────────────────────

public sealed record NotificationView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NotificationType>))]
    [property: JsonPropertyName("type")] NotificationType Type,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("payload")] JsonElement? Payload,
    [property: JsonPropertyName("source_event_id")] Guid? SourceEventId,
    [property: JsonPropertyName("source_type")] string? SourceType,
    [property: JsonPropertyName("read")] bool Read,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public sealed record NotificationPage(
    [property: JsonPropertyName("items")] IReadOnlyList<NotificationView> Items,
    [property: JsonPropertyName("total")] int Total);

// ─── Command ─────────────────────────────────────────────────────────────────

public sealed record RegisterDeviceRequest(
    [property: JsonPropertyName("device_token")] string DeviceToken,
    [property: JsonPropertyName("platform")] string Platform,  // "android" | "ios" | "web"
    [property: JsonPropertyName("provider")] string Provider); // "fcm" | "expo"

public sealed record RegisterDeviceResult(
    [property: JsonPropertyName("device_id")] Guid DeviceId,
    [property: JsonPropertyName("registered_at")] DateTime RegisteredAt);

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
