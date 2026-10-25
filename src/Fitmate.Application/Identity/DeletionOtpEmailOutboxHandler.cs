using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Outbox;

namespace Fitmate.Application.Identity;

public sealed record DeletionOtpEmailPayload(string ProtectedEmail, string ProtectedCode, DateTime ExpiresAt);

public sealed class DeletionOtpEmailOutboxHandler(IEmailSender sender, IDeletionTokenProtector protector) : IOutboxHandler
{
    public string Type => "account.deletion_otp_email";
    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<DeletionOtpEmailPayload>(payloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null || payload.ExpiresAt <= DateTime.UtcNow)
            throw new TerminalOutboxException("Account deletion OTP email expired or invalid.");
        var email = protector.Unprotect(payload.ProtectedEmail);
        var code = protector.Unprotect(payload.ProtectedCode);
        if (code.Length != 6 || code.Any(c => c < '0' || c > '9'))
            throw new TerminalOutboxException("Account deletion OTP format invalid.");
        await sender.SendAsync(new EmailMessage(email, "Fitmate — Mã xác thực yêu cầu xóa tài khoản",
            $"<p>Mã xác thực yêu cầu xóa tài khoản Fitmate:</p><h2>{code}</h2><p>Mã có hiệu lực tối đa 5 phút. Không chia sẻ mã này. Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>",
            $"Mã xác thực yêu cầu xóa tài khoản Fitmate: {code}. Mã có hiệu lực tối đa 5 phút. Không chia sẻ mã này. Nếu bạn không yêu cầu, hãy bỏ qua email này."), ct);
    }
}
