using Fitmate.Application.Email;
using Xunit;

namespace Fitmate.UnitTests.Email;

public class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    [Fact]
    public void RenderPasswordReset_HtmlEncodesFullName_AndContainsTtlAndCode()
    {
        var maliciousName = "<script>alert('xss')</script> John";
        var resetCode = "123456";
        var ttl = TimeSpan.FromMinutes(5);

        var msg = _renderer.RenderPasswordReset("user@example.com", maliciousName, resetCode, ttl);

        Assert.Equal("user@example.com", msg.ToEmail);
        Assert.Contains("[Fitmate]", msg.Subject);
        Assert.DoesNotContain("<script>", msg.HtmlBody);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt; John", msg.HtmlBody);
        Assert.Contains("5 phút", msg.HtmlBody);
        Assert.Contains(resetCode, msg.HtmlBody);
        Assert.Contains(resetCode, msg.PlainTextBody);
        Assert.DoesNotContain("http", msg.HtmlBody);
    }

    [Fact]
    public void RenderAccountDeletion_ContainsWarningAndFormattedExpiryDate()
    {
        var email = "victim@example.com";
        var confirmUrl = "https://fitmate.vn/confirm-account-deletion?token=del-token-123";
        var expiry = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var msg = _renderer.RenderAccountDeletion(email, confirmUrl, expiry);

        Assert.Equal(email, msg.ToEmail);
        Assert.Contains("Xác nhận yêu cầu xóa tài khoản", msg.Subject);
        Assert.Contains("30 ngày", msg.HtmlBody);
        Assert.Contains(confirmUrl, msg.HtmlBody);
        Assert.Contains("31/12/2026", msg.HtmlBody);
        Assert.Contains(confirmUrl, msg.PlainTextBody);
    }

    [Fact]
    public void RenderSupportTicketReply_DoesNotContainSensitiveConversationBody_OnlySafeLink()
    {
        var ticketCode = "TCK-9901";
        var ticketUrl = "https://fitmate.vn/support/tickets/3fa85f64-5717-4562-b3fc-2c963f66afa6";

        var msg = _renderer.RenderSupportTicketReply("member@example.com", "Member Name", ticketCode, ticketUrl);

        Assert.Contains("Phản hồi mới cho yêu cầu hỗ trợ #TCK-9901", msg.Subject);
        Assert.Contains("bảo mật thông tin", msg.HtmlBody);
        Assert.Contains(ticketUrl, msg.HtmlBody);
        Assert.Contains(ticketUrl, msg.PlainTextBody);
    }

    [Fact]
    public void RenderUserSecurityWarning_HtmlEncodesWarningNote()
    {
        var maliciousNote = "<b>Spam detected</b> <img src=x onerror=alert(1)>";
        var now = DateTime.UtcNow;

        var msg = _renderer.RenderUserSecurityWarning("spammer@example.com", "Spammer User", maliciousNote, now);

        Assert.Contains("Cảnh báo an toàn", msg.Subject);
        Assert.DoesNotContain("<img src=x", msg.HtmlBody);
        Assert.Contains("&lt;img src=x", msg.HtmlBody);
    }
}
