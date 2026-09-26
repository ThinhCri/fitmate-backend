using System.Net;

namespace Fitmate.Application.Email;

public class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public EmailMessage RenderPasswordReset(string toEmail, string? fullName, string resetUrl, TimeSpan ttl)
    {
        var safeName = WebUtility.HtmlEncode(fullName ?? "bạn");
        var safeUrl = WebUtility.HtmlEncode(resetUrl);
        var ttlText = ttl.TotalMinutes >= 60
            ? $"{(int)ttl.TotalHours} giờ"
            : $"{(int)ttl.TotalMinutes} phút";

        var subject = "[Fitmate] Đặt lại mật khẩu của bạn";

        var html = $"""
            <h2>Xin chào {safeName},</h2>
            <p>Bạn (hoặc ai đó) đã yêu cầu đặt lại mật khẩu tài khoản Fitmate.</p>
            <p>Nhấn vào liên kết bên dưới để đặt lại mật khẩu. Liên kết có hiệu lực trong <strong>{ttlText}</strong> và chỉ sử dụng được <strong>01 lần</strong>.</p>
            <p><a href="{safeUrl}">{safeUrl}</a></p>
            <p>Nếu bạn không yêu cầu thao tác này, vui lòng bỏ qua email này. Mật khẩu của bạn sẽ không thay đổi.</p>
            <hr/>
            <p style="font-size:12px;color:#888;">Đây là email tự động từ Fitmate. Vui lòng không trả lời email này.</p>
            """;

        var plain = $"""
            Xin chào {fullName ?? "bạn"},

            Bạn (hoặc ai đó) đã yêu cầu đặt lại mật khẩu tài khoản Fitmate.
            Truy cập liên kết bên dưới để đặt lại mật khẩu. Liên kết có hiệu lực trong {ttlText} và chỉ sử dụng được 01 lần.

            {resetUrl}

            Nếu bạn không yêu cầu thao tác này, vui lòng bỏ qua email này.
            """;

        return new EmailMessage(toEmail, subject, html, plain);
    }

    public EmailMessage RenderEmailVerification(string toEmail, string? fullName, string verifyUrl, TimeSpan ttl)
    {
        var safeName = WebUtility.HtmlEncode(fullName ?? "bạn");
        var safeUrl = WebUtility.HtmlEncode(verifyUrl);
        var ttlText = ttl.TotalHours >= 24
            ? $"{(int)(ttl.TotalHours / 24)} ngày"
            : $"{(int)ttl.TotalHours} giờ";

        var subject = "[Fitmate] Xác minh địa chỉ email của bạn";

        var html = $"""
            <h2>Chào mừng {safeName} đến với Fitmate!</h2>
            <p>Vui lòng xác minh địa chỉ email bằng cách nhấn vào liên kết bên dưới. Liên kết có hiệu lực trong <strong>{ttlText}</strong>.</p>
            <p><a href="{safeUrl}">{safeUrl}</a></p>
            <hr/>
            <p style="font-size:12px;color:#888;">Đây là email tự động từ Fitmate. Vui lòng không trả lời email này.</p>
            """;

        var plain = $"""
            Chào mừng {fullName ?? "bạn"} đến với Fitmate!

            Vui lòng xác minh địa chỉ email bằng cách truy cập liên kết sau. Liên kết có hiệu lực trong {ttlText}.

            {verifyUrl}
            """;

        return new EmailMessage(toEmail, subject, html, plain);
    }

    public EmailMessage RenderAccountDeletion(string toEmail, string confirmUrl, DateTime expiresAtUtc)
    {
        var safeUrl = WebUtility.HtmlEncode(confirmUrl);
        var expiryFormatted = expiresAtUtc.ToString("dd/MM/yyyy HH:mm") + " UTC";

        var subject = "[Fitmate] Xác nhận yêu cầu xóa tài khoản";

        var html = $"""
            <h2>Xác nhận xóa tài khoản Fitmate</h2>
            <p>Chúng tôi nhận được yêu cầu xóa tài khoản của bạn. Đây là hành động <strong>không thể hoàn tác</strong>.</p>
            <p>Sau khi xác nhận, dữ liệu cá nhân sẽ bị xóa trong vòng <strong>30 ngày</strong> theo chính sách bảo mật.</p>
            <p>Liên kết xác nhận có hiệu lực đến: <strong>{expiryFormatted}</strong></p>
            <p><a href="{safeUrl}">{safeUrl}</a></p>
            <p>Nếu bạn không yêu cầu xóa tài khoản, vui lòng bỏ qua email này.</p>
            <hr/>
            <p style="font-size:12px;color:#888;">Đây là email tự động từ Fitmate. Vui lòng không trả lời email này.</p>
            """;

        var plain = $"""
            Xác nhận xóa tài khoản Fitmate

            Chúng tôi nhận được yêu cầu xóa tài khoản của bạn. Đây là hành động không thể hoàn tác.
            Sau khi xác nhận, dữ liệu cá nhân sẽ bị xóa trong vòng 30 ngày.
            Liên kết xác nhận có hiệu lực đến: {expiryFormatted}

            {confirmUrl}

            Nếu bạn không yêu cầu xóa tài khoản, vui lòng bỏ qua email này.
            """;

        return new EmailMessage(toEmail, subject, html, plain);
    }

    public EmailMessage RenderSupportTicketReply(string toEmail, string? fullName, string ticketCode, string ticketUrl)
    {
        var safeName = WebUtility.HtmlEncode(fullName ?? "bạn");
        var safeUrl = WebUtility.HtmlEncode(ticketUrl);
        var safeCode = WebUtility.HtmlEncode(ticketCode);

        var subject = $"[Fitmate] Phản hồi mới cho yêu cầu hỗ trợ #{ticketCode}";

        var html = $"""
            <h2>Xin chào {safeName},</h2>
            <p>Yêu cầu hỗ trợ <strong>#{safeCode}</strong> của bạn đã nhận được phản hồi mới từ đội ngũ Fitmate.</p>
            <p>Vì lý do bảo mật thông tin, nội dung phản hồi không hiển thị trong email. Vui lòng đăng nhập để xem chi tiết:</p>
            <p><a href="{safeUrl}">Xem phản hồi</a></p>
            <hr/>
            <p style="font-size:12px;color:#888;">Đây là email tự động từ Fitmate. Vui lòng không trả lời email này.</p>
            """;

        var plain = $"""
            Xin chào {fullName ?? "bạn"},

            Yêu cầu hỗ trợ #{ticketCode} của bạn đã nhận được phản hồi mới.
            Vì lý do bảo mật thông tin, nội dung phản hồi không hiển thị trong email.

            Xem phản hồi: {ticketUrl}
            """;

        return new EmailMessage(toEmail, subject, html, plain);
    }

    public EmailMessage RenderUserSecurityWarning(string toEmail, string? fullName, string warningNote, DateTime issuedAtUtc)
    {
        var safeName = WebUtility.HtmlEncode(fullName ?? "bạn");
        var safeNote = WebUtility.HtmlEncode(warningNote);
        var dateFormatted = issuedAtUtc.ToString("dd/MM/yyyy HH:mm") + " UTC";

        var subject = "[Fitmate] Cảnh báo an toàn tài khoản";

        var html = $"""
            <h2>Xin chào {safeName},</h2>
            <p>Tài khoản của bạn đã nhận được cảnh báo an toàn vào lúc <strong>{dateFormatted}</strong>.</p>
            <p><strong>Nội dung cảnh báo:</strong> {safeNote}</p>
            <p>Nếu bạn tin rằng đây là nhầm lẫn, vui lòng liên hệ đội ngũ hỗ trợ Fitmate.</p>
            <hr/>
            <p style="font-size:12px;color:#888;">Đây là email tự động từ Fitmate. Vui lòng không trả lời email này.</p>
            """;

        var plain = $"""
            Xin chào {fullName ?? "bạn"},

            Tài khoản của bạn đã nhận được cảnh báo an toàn vào lúc {dateFormatted}.
            Nội dung cảnh báo: {warningNote}

            Nếu bạn tin rằng đây là nhầm lẫn, vui lòng liên hệ đội ngũ hỗ trợ Fitmate.
            """;

        return new EmailMessage(toEmail, subject, html, plain);
    }
}
