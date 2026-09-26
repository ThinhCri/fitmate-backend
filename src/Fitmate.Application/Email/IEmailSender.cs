namespace Fitmate.Application.Email;

/// <summary>
/// Gửi email qua SMTP. Adapter MailKit implement ở Infrastructure.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
