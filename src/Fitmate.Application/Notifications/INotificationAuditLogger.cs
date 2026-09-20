namespace Fitmate.Application.Notifications;

/// <summary>
/// Audit log abstraction for push delivery events.
/// </summary>
public interface INotificationAuditLogger
{
    /// <summary>
    /// Log a structured audit entry for a push-related event.
    /// </summary>
    /// <param name="action">Short action string, e.g. <c>notification.skipped</c>,
    /// <c>push.sent</c>, <c>push.failed</c>.</param>
    /// <param name="userId">Affected user id.</param>
    /// <param name="metadata">Structured key-value pairs. Never include raw tokens,
    /// decrypted content, or secrets.</param>
    void Log(string action, Guid userId, IReadOnlyDictionary<string, string> metadata);
}
