using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Email.Handlers;

public class AccountDeletionEmailOutboxHandler : IOutboxHandler
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly EmailOptions _options;
    private readonly ILogger<AccountDeletionEmailOutboxHandler> _logger;

    public string Type => "account.deletion_verification_email";

    public AccountDeletionEmailOutboxHandler(
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        IOptions<EmailOptions> options,
        ILogger<AccountDeletionEmailOutboxHandler> logger)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AccountDeletionEmailPayloadDto>(payloadJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new TerminalOutboxException("Invalid account deletion outbox payload");

        if (string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.Token))
        {
            throw new TerminalOutboxException("Account deletion payload missing required email or token");
        }

        // Kiểm tra hạn sử dụng của token
        if (payload.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogWarning("Account deletion verification token for user {UserId} expired at {ExpiresAt}. Dropping stale email.",
                payload.UserId, payload.ExpiresAt);
            throw new TerminalOutboxException("TOKEN_EXPIRED: Account deletion token expired before email dispatch");
        }

        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var confirmUrl = $"{baseUrl}/confirm-account-deletion?token={Uri.EscapeDataString(payload.Token)}";

        var message = _templateRenderer.RenderAccountDeletion(payload.Email, confirmUrl, payload.ExpiresAt);
        await _emailSender.SendAsync(message, ct);

        _logger.LogInformation("Account deletion confirmation email sent successfully to user {UserId}", payload.UserId);
    }
}

public sealed record AccountDeletionEmailPayloadDto(
    Guid UserId,
    string Email,
    string Token,
    DateTime ExpiresAt,
    string? Reason);
