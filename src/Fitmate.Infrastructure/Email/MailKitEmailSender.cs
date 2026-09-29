using Fitmate.Application.Email;
using Fitmate.Application.Outbox;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Fitmate.Infrastructure.Email;

public class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IOptions<EmailOptions> options, ILogger<MailKitEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.SenderName, _options.SenderEmail));
        mime.To.Add(MailboxAddress.Parse(message.ToEmail));
        mime.Subject = message.Subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.PlainTextBody
        };
        mime.Body = bodyBuilder.ToMessageBody();

        var socketOptions = _options.TlsMode switch
        {
            SmtpTlsMode.None => SecureSocketOptions.None,
            SmtpTlsMode.Auto => SecureSocketOptions.Auto,
            SmtpTlsMode.StartTls => SecureSocketOptions.StartTls,
            SmtpTlsMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls
        };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
            {
                if (string.IsNullOrWhiteSpace(_options.SmtpPassword))
                {
                    throw new TerminalOutboxException(
                        "SMTP_CONFIG_ERROR: Email:SmtpPassword is required when Email:SmtpUsername is configured");
                }

                await client.AuthenticateAsync(_options.SmtpUsername, _options.SmtpPassword, ct);
            }

            await client.SendAsync(mime, ct);
            _logger.LogDebug("Email sent to {Recipient} via {Host}:{Port}", message.ToEmail, _options.SmtpHost, _options.SmtpPort);
        }
        catch (SmtpCommandException ex) when (IsTransient(ex))
        {
            _logger.LogWarning(ex, "Transient SMTP error sending to {Recipient}: {StatusCode}",
                message.ToEmail, ex.StatusCode);
            throw; // OutboxWorker sẽ retry với backoff
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "Permanent SMTP error sending to {Recipient}: {StatusCode}",
                message.ToEmail, ex.StatusCode);
            throw new TerminalOutboxException(
                $"SMTP_PERMANENT: {ex.StatusCode} - {SanitizeSmtpError(ex.Message)}", ex);
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogError(ex, "SMTP protocol error sending to {Recipient}", message.ToEmail);
            throw new TerminalOutboxException(
                $"SMTP_PROTOCOL_ERROR: {SanitizeSmtpError(ex.Message)}", ex);
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP authentication failed for {Host}", _options.SmtpHost);
            throw new TerminalOutboxException(
                "SMTP_AUTH_FAILED: Authentication credentials rejected by server", ex);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, ct);
            }
        }
    }

    /// <summary>
    /// Transient SMTP errors: 4xx status codes are typically temporary.
    /// 5xx are permanent (bad recipient, policy rejection, etc.).
    /// </summary>
    private static bool IsTransient(SmtpCommandException ex)
    {
        var code = (int)ex.StatusCode;
        return code >= 400 && code < 500;
    }

    private static string SanitizeSmtpError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return string.Empty;
        // Loại bỏ thông tin nhạy cảm có thể leak từ server response
        var s = System.Text.RegularExpressions.Regex.Replace(
            error, @"(?i)(password|token|secret|key)=\S+", "$1=[REDACTED]");
        return s.Length > 500 ? s[..500] : s;
    }
}
