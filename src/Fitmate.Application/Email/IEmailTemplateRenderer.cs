namespace Fitmate.Application.Email;

/// <summary>
/// Render nội dung email HTML + plain text tiếng Việt.
/// Tất cả input text phải được HTML-encode trong HtmlBody.
/// </summary>
public interface IEmailTemplateRenderer
{
    EmailMessage RenderPasswordReset(string toEmail, string? fullName, string resetUrl, TimeSpan ttl);
    EmailMessage RenderEmailVerification(string toEmail, string? fullName, string verifyUrl, TimeSpan ttl);
    EmailMessage RenderAccountDeletion(string toEmail, string confirmUrl, DateTime expiresAtUtc);
    EmailMessage RenderSupportTicketReply(string toEmail, string? fullName, string ticketCode, string ticketUrl);
    EmailMessage RenderUserSecurityWarning(string toEmail, string? fullName, string warningNote, DateTime issuedAtUtc);
}
