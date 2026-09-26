namespace Fitmate.Application.Email;

/// <summary>
/// Mã hóa/giải mã token nhạy cảm (password reset, email verification)
/// trước khi lưu vào outbox payload. Dùng DataProtection ở Infrastructure.
/// </summary>
public interface IEmailTokenProtector
{
    string Protect(string plainToken);
    string Unprotect(string protectedToken);
}
