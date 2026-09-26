using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Fitmate.Application.Email.Handlers;

public class SecurityWarningEmailOutboxHandler : IOutboxHandler
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<SecurityWarningEmailOutboxHandler> _logger;

    public string Type => "user.warning";

    public SecurityWarningEmailOutboxHandler(
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        UserManager<ApplicationUser> userManager,
        ILogger<SecurityWarningEmailOutboxHandler> logger)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<UserWarningPayloadDto>(payloadJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new TerminalOutboxException("Invalid user warning outbox payload");

        var user = await _userManager.FindByIdAsync(payload.TargetUserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning("Target user {UserId} for security warning not found or has no email", payload.TargetUserId);
            throw new TerminalOutboxException("USER_NOT_FOUND: Recipient user for security warning not found");
        }

        var message = _templateRenderer.RenderUserSecurityWarning(
            user.Email,
            user.FullName,
            payload.Note ?? "Vi phạm tiêu chuẩn cộng đồng hoặc quy định an toàn Fitmate.",
            DateTime.UtcNow);

        await _emailSender.SendAsync(message, ct);

        _logger.LogInformation("Security warning email sent to user {UserId}", payload.TargetUserId);
    }
}

public sealed record UserWarningPayloadDto(
    Guid TargetUserId,
    Guid? ReportId,
    string? Note,
    Guid AdminUserId);
