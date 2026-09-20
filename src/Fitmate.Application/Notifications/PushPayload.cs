using System.Text.Json;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;

namespace Fitmate.Application.Notifications;

/// <summary>
/// Validates notification payloads against per-type allowlists and enforces
/// size limits. Call this before serializing and storing in
/// <see cref="Notification"/>.
/// </summary>
public static class PushPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // ─── Size limits ─────────────────────────────────────────────────────────

    public const int MaxPayloadBytes = 4096; // UTF-8 bytes
    public const int MaxTitleLength = 120;
    public const int MaxMessageLength = 500;
    public const int MaxSummaryLength = 120;

    // ─── Per-type allowlists ────────────────────────────────────────────────

    /// <summary>Allowed top-level fields per notification type.</summary>
    private static readonly Dictionary<NotificationType, HashSet<string>> Allowlist = new()
    {
        [NotificationType.FriendRequest] = new(StringComparer.OrdinalIgnoreCase)
        {
            "requestId",
            "senderId",
        },
        [NotificationType.System] = new(StringComparer.OrdinalIgnoreCase)
        {
            "reportId",
        },
        // Booking and payment payloads are out of scope for BE-200.
        // They will be added when BE-200b is implemented.
        [NotificationType.Booking] = new(StringComparer.OrdinalIgnoreCase)
        {
            "appointmentId",
        },
        [NotificationType.Payment] = new(StringComparer.OrdinalIgnoreCase)
        {
            "paymentId",
        },
        [NotificationType.Reminder] = new(StringComparer.OrdinalIgnoreCase)
        {
            "appointmentId",
        },
    };

    // ─── Validation ────────────────────────────────────────────────────────

    /// <summary>
    /// Validate a notification payload and serialize to JSON.
    /// Throws <see cref="ArgumentException"/> with a descriptive code if validation fails.
    /// </summary>
    /// <param name="type">Notification type.</param>
    /// <param name="payload">Domain object to validate and serialize.</param>
    /// <returns>JSON string. Guaranteed to fit within <see cref="MaxPayloadBytes"/>.</returns>
    public static string SerializeAndValidate(NotificationType type, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        // Size check — measure UTF-8 byte count
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(json);
        if (byteCount > MaxPayloadBytes)
            throw new ArgumentException(
                $"Payload exceeds {MaxPayloadBytes} UTF-8 bytes ({byteCount} bytes).",
                nameof(payload));

        // Allowlist check
        if (Allowlist.TryGetValue(type, out var allowed))
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!allowed.Contains(prop.Name))
                    throw new ArgumentException(
                        $"Field '{prop.Name}' is not allowed for notification type '{type}'.",
                        nameof(payload));
            }
        }

        return json;
    }

    /// <summary>Validate title length.</summary>
    public static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title must not be empty.", nameof(title));
        if (title.Length > MaxTitleLength)
            throw new ArgumentException(
                $"Title exceeds {MaxTitleLength} characters ({title.Length}).",
                nameof(title));
    }

    /// <summary>Validate message length.</summary>
    public static void ValidateMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message must not be empty.", nameof(message));
        if (message.Length > MaxMessageLength)
            throw new ArgumentException(
                $"Message exceeds {MaxMessageLength} characters ({message.Length}).",
                nameof(message));
    }

    /// <summary>Validate optional summary length.</summary>
    public static void ValidateSummary(string? summary)
    {
        if (summary is { Length: > MaxSummaryLength })
            throw new ArgumentException(
                $"Summary exceeds {MaxSummaryLength} characters ({summary.Length}).",
                nameof(summary));
    }
}
