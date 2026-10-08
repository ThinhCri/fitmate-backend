using System.Text.RegularExpressions;
using Fitmate.Application.Common;
using Fitmate.Domain.Common;

namespace Fitmate.Application.Auth;

public sealed record PhoneVerificationStartResult(DateTime ExpiresAtUtc, int RetryAfterSeconds);

public interface IPhoneVerificationGateway
{
    Task<PhoneVerificationStartResult> StartAsync(Guid userId, string phoneNumberE164, CancellationToken ct);
    Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken ct);
}

/// <summary>PT-only phone ownership check; successful SMS dispatch never implies verification.</summary>
public sealed class PhoneVerificationService(
    IAuthorizationGuard authorization,
    IPhoneVerificationGateway gateway,
    ISmsOtpSender sender)
{
    private static readonly Regex VietnamMobile = new(@"^\+84[1-9][0-9]{8}$", RegexOptions.Compiled);

    public async Task<PhoneVerificationStartResult> StartAsync(Guid actorId, string phoneNumberE164, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(actorId, "PT", ct);
        if (string.IsNullOrWhiteSpace(phoneNumberE164) || !VietnamMobile.IsMatch(phoneNumberE164))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Số điện thoại PT phải theo định dạng +84xxxxxxxxx.",
                new Dictionary<string, string[]> { ["phone_number"] = ["invalid E.164 Vietnam mobile"] });
        if (!sender.IsConfigured)
            throw new DomainException("SMS_PROVIDER_NOT_CONFIGURED", "Dịch vụ SMS xác minh số điện thoại chưa được cấu hình.", 503);
        return await gateway.StartAsync(actorId, phoneNumberE164, ct);
    }

    public async Task ConfirmAsync(Guid actorId, string code, CancellationToken ct)
    {
        await authorization.EnsureRoleAsync(actorId, "PT", ct);
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid,
                "Mã OTP phải gồm 6 chữ số.",
                new Dictionary<string, string[]> { ["code"] = ["six digits required"] });
        if (!await gateway.ConfirmAsync(actorId, code, ct))
            throw new DomainException("PHONE_CODE_INVALID", "Mã xác minh không đúng, đã hết hạn hoặc đã dùng.", 400);
    }
}
