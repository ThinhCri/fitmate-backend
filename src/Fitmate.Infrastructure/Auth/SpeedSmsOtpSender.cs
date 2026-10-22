using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Auth;
using Fitmate.Application.Outbox;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Auth;

/// <summary>SpeedSMS transport only. OTP generation, expiry and attempt limits remain in Identity.</summary>
public sealed class SpeedSmsOtpSender(HttpClient http, IOptions<SpeedSmsOptions> options) : ISmsOtpSender
{
    private const string Endpoint = "https://api.speedsms.vn/index.php/sms/send";

    public bool IsConfigured => options.Value.IsConfigured();

    public async Task SendAsync(string phoneNumberE164, string code, CancellationToken ct)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("SMS_PROVIDER_NOT_CONFIGURED");
        if (!phoneNumberE164.StartsWith("+84", StringComparison.Ordinal) ||
            phoneNumberE164.Length != 12 || !phoneNumberE164.AsSpan(1).ToString().All(char.IsDigit))
            throw new TerminalOutboxException("SMS_PHONE_INVALID");
        if (code.Length != 6 || !code.All(char.IsDigit))
            throw new TerminalOutboxException("SMS_CODE_INVALID");

        var config = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        // Do not put the token or OTP in the URL: reverse proxies and access logs often record it.
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(config.AccessToken + ":x")));
        request.Content = JsonContent.Create(new
        {
            // SpeedSMS documents Vietnamese recipients in local 0xxxxxxxxx format.
            to = new[] { "0" + phoneNumberE164[3..] },
            content = $"[Fitmate] Ma xac minh PT cua ban: {code}. Hieu luc 5 phut. Khong chia se ma.",
            // The provider's downloadable C# sendSMS implementation serializes this as "type".
            type = config.SmsType,
            sender = config.Sender
        });

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("SMS_PROVIDER_HTTP_FAILURE");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var result = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = result.RootElement;
        var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var providerCode = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        if (status == "success" && providerCode == "00")
        {
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("invalidPhone", out var invalidPhone) &&
                invalidPhone.ValueKind == JsonValueKind.Array && invalidPhone.GetArrayLength() > 0)
                throw new TerminalOutboxException("SMS_PHONE_REJECTED");
            return;
        }

        // Permanent request/recipient errors cannot succeed by replaying the same outbox item.
        if (providerCode is "101" or "105" or "110" or "113")
            throw new TerminalOutboxException("SMS_PROVIDER_REJECTED_REQUEST");
        if (providerCode is "007" or "008" or "009")
            throw new TerminalOutboxException("SMS_PROVIDER_ACCESS_DENIED_" + providerCode);
        if (providerCode == "300")
            throw new TerminalOutboxException("SMS_PROVIDER_INSUFFICIENT_BALANCE");
        // The numeric provider code is safe to retain; never log its free-form message.
        if (providerCode is { Length: > 0 and <= 3 } && providerCode.All(char.IsAsciiDigit))
            throw new InvalidOperationException("SMS_PROVIDER_UNAVAILABLE_" + providerCode);
        // Only expose a small fixed diagnostic vocabulary, never provider text or recipient details.
        var statusKind = status switch
        {
            "success" => "SUCCESS",
            "error" => "ERROR",
            _ => "OTHER"
        };
        var codeKind = providerCode is null ? "MISSING" :
            providerCode.Length == 0 ? "EMPTY" :
            providerCode.Length <= 20 && providerCode.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                ? providerCode.ToUpperInvariant() : "UNSAFE";
        throw new InvalidOperationException($"SMS_PROVIDER_UNAVAILABLE_STATUS_{statusKind}_CODE_{codeKind}");
    }
}
