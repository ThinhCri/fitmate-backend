using Fitmate.Application.Auth;

namespace Fitmate.Infrastructure.Auth;

/// <summary>Fail closed until a real SMS provider is configured.</summary>
public sealed class UnconfiguredSmsOtpSender : ISmsOtpSender
{
    public bool IsConfigured => false;
    public Task SendAsync(string phoneNumberE164, string code, CancellationToken ct) =>
        throw new InvalidOperationException("SMS_PROVIDER_NOT_CONFIGURED");
}
