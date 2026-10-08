using System.Text.Json;
using Fitmate.Application.Outbox;

namespace Fitmate.Application.Auth;

public sealed record PhoneOtpOutboxPayload(Guid UserId, string ProtectedPhone, string ProtectedCode, DateTime ExpiresAtUtc);

public sealed class PhoneOtpOutboxHandler(ISmsOtpSender sender, IPhoneOtpProtector protector) : IOutboxHandler
{
    public string Type => "auth.phone_verification";

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PhoneOtpOutboxPayload>(payloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new TerminalOutboxException("Invalid phone verification payload.");
        if (payload.ExpiresAtUtc <= DateTime.UtcNow)
            throw new TerminalOutboxException("PHONE_CODE_EXPIRED");
        if (string.IsNullOrWhiteSpace(payload.ProtectedPhone) || string.IsNullOrWhiteSpace(payload.ProtectedCode))
            throw new TerminalOutboxException("Invalid phone verification payload.");
        try
        {
            await sender.SendAsync(protector.Unprotect(payload.ProtectedPhone),
                protector.Unprotect(payload.ProtectedCode), ct);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new TerminalOutboxException("PHONE_CODE_UNPROTECT_FAILED", ex);
        }
    }
}
