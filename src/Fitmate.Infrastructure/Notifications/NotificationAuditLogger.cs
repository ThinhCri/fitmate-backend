using Fitmate.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>
/// Infrastructure implementation of <see cref="INotificationAuditLogger"/>.
/// Writes structured audit events using ILogger so they are captured in the
/// structured log sink (Serilog JSON → stdout → Loki / Datadog).
///
/// <para>
/// IMPORTANT: Never log raw tokens, decrypted content, provider payloads,
/// or any secret. All logged metadata is pre-approved short strings.
/// </para>
/// </summary>
public class NotificationAuditLogger : INotificationAuditLogger
{
    private readonly ILogger<NotificationAuditLogger> _logger;

    public NotificationAuditLogger(ILogger<NotificationAuditLogger> logger)
        => _logger = logger;

    public void Log(string action, Guid userId, IReadOnlyDictionary<string, string> metadata)
    {
        // Structured log: Serilog captures key-value pairs natively.
        _logger.LogInformation(
            "Audit: {AuditAction} user={UserId} {Metadata}",
            action,
            userId,
            metadata.Select(kv => $"{kv.Key}={kv.Value}").ToList());
    }
}
