namespace Fitmate.Application.Auth;

/// <summary>
/// Provider-neutral transport for one-time phone verification codes.
/// Implementations must not log the code or recipient in plaintext.
/// A successful return means the provider accepted the message, not that the phone was verified.
/// </summary>
public interface ISmsOtpSender
{
    bool IsConfigured { get; }
    Task SendAsync(string phoneNumberE164, string code, CancellationToken ct);
}
