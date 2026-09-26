using System.ComponentModel.DataAnnotations;

namespace Fitmate.Application.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string SmtpHost { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    public SmtpTlsMode TlsMode { get; set; } = SmtpTlsMode.StartTls;

    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }

    [Required, EmailAddress]
    public string SenderEmail { get; set; } = string.Empty;

    [Required]
    public string SenderName { get; set; } = "Fitmate";

    /// <summary>
    /// URL gốc frontend để tạo liên kết trong email (ví dụ: https://fitmate.vn).
    /// Bắt buộc có giá trị hợp lệ, KHÔNG lấy từ request Host.
    /// </summary>
    [Required]
    public string FrontendBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Kiểm tra cấu hình cho Production. Gọi khi app startup.
    /// Fail-closed nếu thiếu thông tin bắt buộc.
    /// </summary>
    public void ValidateForProduction()
    {
        if (string.IsNullOrWhiteSpace(SmtpHost))
            throw new InvalidOperationException("Email:SmtpHost is required in Production.");

        if (TlsMode == SmtpTlsMode.None || TlsMode == SmtpTlsMode.Auto)
            throw new InvalidOperationException(
                $"Email:TlsMode '{TlsMode}' is not allowed in Production. Use StartTls or SslOnConnect.");

        if (string.IsNullOrWhiteSpace(SenderEmail))
            throw new InvalidOperationException("Email:SenderEmail is required in Production.");

        if (string.IsNullOrWhiteSpace(FrontendBaseUrl))
            throw new InvalidOperationException("Email:FrontendBaseUrl is required in Production.");

        if (!Uri.TryCreate(FrontendBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http"))
            throw new InvalidOperationException(
                $"Email:FrontendBaseUrl '{FrontendBaseUrl}' is not a valid absolute HTTP(S) URL.");
    }
}

/// <summary>
/// Chế độ TLS cho kết nối SMTP.
/// Production phải dùng StartTls hoặc SslOnConnect.
/// None và Auto bị cấm trên Production.
/// </summary>
public enum SmtpTlsMode
{
    /// <summary>Không mã hóa. CHỈ dùng cho local dev/test.</summary>
    None = 0,

    /// <summary>
    /// MailKit Auto — có thể fallback không mã hóa nếu server không hỗ trợ.
    /// CHỈ dùng cho local dev/test.
    /// </summary>
    Auto = 1,

    /// <summary>STARTTLS trên cổng 587 (khuyến nghị).</summary>
    StartTls = 2,

    /// <summary>SSL/TLS ngay từ đầu trên cổng 465.</summary>
    SslOnConnect = 3
}
