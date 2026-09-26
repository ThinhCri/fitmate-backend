using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Email.Handlers;

public class SupportReplyEmailOutboxHandler : IOutboxHandler
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EmailOptions _options;
    private readonly ILogger<SupportReplyEmailOutboxHandler> _logger;

    public string Type => "support.ticket_replied";

    public SupportReplyEmailOutboxHandler(
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        UserManager<ApplicationUser> userManager,
        IOptions<EmailOptions> options,
        ILogger<SupportReplyEmailOutboxHandler> logger)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _userManager = userManager;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SupportTicketRepliedPayloadDto>(payloadJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new TerminalOutboxException("Invalid support ticket reply outbox payload");

        var user = await _userManager.FindByIdAsync(payload.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning("User {UserId} associated with ticket {TicketId} not found or has no email",
                payload.UserId, payload.TicketId);
            throw new TerminalOutboxException("USER_NOT_FOUND: Recipient user for support reply not found");
        }

        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var ticketCode = string.IsNullOrWhiteSpace(payload.TicketCode)
            ? payload.TicketId.ToString()[..8].ToUpperInvariant()
            : payload.TicketCode;
        var ticketUrl = $"{baseUrl}/support/tickets/{payload.TicketId}";

        var message = _templateRenderer.RenderSupportTicketReply(user.Email, user.FullName, ticketCode, ticketUrl);
        await _emailSender.SendAsync(message, ct);

        _logger.LogInformation("Support reply notification email sent for ticket {TicketId} to user {UserId}",
            payload.TicketId, payload.UserId);
    }
}

public sealed record SupportTicketRepliedPayloadDto(
    Guid TicketId,
    Guid UserId,
    Guid AdminUserId,
    string? TicketCode,
    DateTime CreatedAtUtc);
