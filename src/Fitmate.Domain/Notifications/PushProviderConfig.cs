using Fitmate.Domain.Common;

namespace Fitmate.Domain.Notifications;

/// <summary>
/// Singleton runtime configuration for the push subsystem. Worker reads
/// <see cref="Enabled"/> and the optional credentials blob to decide whether
/// to actually send, defer, or mark <c>FailedPermanent</c> with reason
/// <c>push_provider_not_configured</c>. See DEC-040 + DEC-044.
/// </summary>
public class PushProviderConfig : AuditableEntity
{
    /// <summary>Singleton id; only row <c>1</c> is read.</summary>
    public int Id { get; private set; } = 1;

    public bool Enabled { get; private set; }

    /// <summary>Currently selected provider; defaults to <see cref="PushProvider.Fcm"/> when enabled.</summary>
    public PushProvider Provider { get; private set; } = PushProvider.Fcm;

    /// <summary>
    /// Provider-specific configuration blob (JSON). Sensitive values
    /// (service account JSON, API keys, ...) MUST be referenced via secret
    /// manager — only the URI/key id is stored here.
    /// </summary>
    public string? ConfigJson { get; private set; }

    private PushProviderConfig() { }

    public static PushProviderConfig Disabled() => new()
    {
        Id = 1,
        Enabled = false,
        Provider = PushProvider.Fcm,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    public void Enable(PushProvider provider, string? configJson)
    {
        Enabled = true;
        Provider = provider;
        ConfigJson = configJson;
        Touch();
    }

    public void Disable()
    {
        Enabled = false;
        ConfigJson = null;
        Touch();
    }
}
