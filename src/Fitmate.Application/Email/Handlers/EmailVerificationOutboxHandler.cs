using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Email.Handlers;

public class EmailVerificationOutboxHandler : IOutboxHandler
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IEmailTokenProtector _tokenProtector;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailVerificationOutboxHandler> _logger;

    public string Type => "auth.email_verification";

    public EmailVerificationOutboxHandler(
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        IEmailTokenProtector tokenProtector,
        IOptions<EmailOptions> options,
        ILogger<EmailVerificationOutboxHandler> logger)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _tokenProtector = tokenProtector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EmailVerificationPayloadDto>(payloadJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new TerminalOutboxException("Invalid email verification outbox payload");

        if (string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.ProtectedToken))
        {
            throw new TerminalOutboxException("Email verification payload missing required email or token");
        }

        var ttl = TimeSpan.FromHours(24);
        if (payload.ExpiresAtUtc.HasValue && payload.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            _logger.LogWarning("Email verification token for user {UserId} expired at {ExpiresAt}. Dropping stale email.",
                payload.UserId, payload.ExpiresAtUtc.Value);
            throw new TerminalOutboxException("TOKEN_EXPIRED: Email verification token has expired");
        }

        string plainToken;
        try
        {
            plainToken = _tokenProtector.Unprotect(payload.ProtectedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect verification token for user {UserId}", payload.UserId);
            throw new TerminalOutboxException("TOKEN_UNPROTECT_FAILED: Cryptographic verification token invalid", ex);
        }

        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var verifyUrl = $"{baseUrl}/verify-email?token={Uri.EscapeDataString(plainToken)}&userId={payload.UserId}";

        var message = _templateRenderer.RenderEmailVerification(payload.Email, payload.FullName, verifyUrl, ttl);
        await _emailSender.SendAsync(message, ct);

        _logger.LogInformation("Email verification dispatched successfully to user {UserId}", payload.UserId);
    }
}

public sealed record EmailVerificationPayloadDto(
    Guid UserId,
    string Email,
    string ProtectedToken,
    string? FullName,
    DateTime? ExpiresAtUtc);
