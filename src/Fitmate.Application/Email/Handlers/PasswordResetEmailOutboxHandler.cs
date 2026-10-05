using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fitmate.Application.Email.Handlers;

public class PasswordResetEmailOutboxHandler : IOutboxHandler
{
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _templateRenderer;
    private readonly IEmailTokenProtector _tokenProtector;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PasswordResetEmailOutboxHandler> _logger;

    public string Type => "auth.password_reset_requested";

    public PasswordResetEmailOutboxHandler(
        IEmailSender emailSender,
        IEmailTemplateRenderer templateRenderer,
        IEmailTokenProtector tokenProtector,
        UserManager<ApplicationUser> userManager,
        IOptions<EmailOptions> options,
        ILogger<PasswordResetEmailOutboxHandler> logger)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _tokenProtector = tokenProtector;
        _userManager = userManager;
        _ = options;
        _logger = logger;
    }

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PasswordResetPayloadDto>(payloadJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new TerminalOutboxException("Invalid password reset outbox payload");

        if (string.IsNullOrWhiteSpace(payload.Email) || string.IsNullOrWhiteSpace(payload.ProtectedToken))
        {
            throw new TerminalOutboxException("Password reset payload missing required email or token");
        }

        // 1. Kiểm tra hạn dùng của token nếu có metadata thời gian
        var ttl = TimeSpan.FromMinutes(5);
        if (payload.ExpiresAtUtc.HasValue && payload.ExpiresAtUtc.Value <= DateTime.UtcNow)
        {
            _logger.LogWarning("Password reset token for user {UserId} already expired at {ExpiresAt}. Dropping stale email.",
                payload.UserId, payload.ExpiresAtUtc.Value);
            throw new TerminalOutboxException("TOKEN_EXPIRED: Password reset token has expired before email dispatch");
        }

        // 2. Kiểm tra trạng thái tài khoản: Nếu user đã bị khóa hoặc xóa -> terminal failure
        var user = await _userManager.FindByIdAsync(payload.UserId.ToString());
        if (user is null || user.IsDeleted || user.Status == Domain.Common.UserStatus.Locked)
        {
            _logger.LogWarning("Password reset target user {UserId} is inactive, deleted, or locked. Aborting email dispatch.", payload.UserId);
            throw new TerminalOutboxException("USER_INACTIVE_OR_LOCKED: Cannot send password reset to inactive or locked user");
        }

        // 3. Giải mã token an toàn
        string plainToken;
        try
        {
            plainToken = _tokenProtector.Unprotect(payload.ProtectedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unprotect password reset token for user {UserId}", payload.UserId);
            throw new TerminalOutboxException("TOKEN_UNPROTECT_FAILED: Cryptographic token validation failed", ex);
        }

        // 4. Gửi mã ngắn để người dùng nhập trực tiếp trong ứng dụng.
        var message = _templateRenderer.RenderPasswordReset(payload.Email, payload.FullName, plainToken, ttl);
        await _emailSender.SendAsync(message, ct);

        _logger.LogInformation("Password reset email sent successfully to user {UserId}", payload.UserId);
    }
}

public sealed record PasswordResetPayloadDto(
    Guid UserId,
    string Email,
    string ProtectedToken,
    string? FullName,
    DateTime? ExpiresAtUtc);
