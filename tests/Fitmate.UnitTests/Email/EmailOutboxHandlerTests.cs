using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Email.Handlers;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Fitmate.UnitTests.Email;

public class EmailOutboxHandlerTests
{
    private readonly Mock<IEmailSender> _sender = new();
    private readonly Mock<IEmailTemplateRenderer> _renderer = new();
    private readonly Mock<IEmailTokenProtector> _protector = new();
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly IOptions<EmailOptions> _options;

    public EmailOutboxHandlerTests()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        _userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _options = Options.Create(new EmailOptions
        {
            FrontendBaseUrl = "https://fitmate.vn",
            SmtpHost = "localhost"
        });
    }

    [Fact]
    public async Task PasswordResetHandler_ExpiredToken_ThrowsTerminal()
    {
        var handler = new PasswordResetEmailOutboxHandler(
            _sender.Object, _renderer.Object, _protector.Object,
            _userManager.Object, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            Guid.NewGuid(), "test@example.com", "protected-token", "Test",
            DateTime.UtcNow.AddMinutes(-5)); // already expired

        var json = JsonSerializer.Serialize(payload);
        var ex = await Assert.ThrowsAsync<TerminalOutboxException>(
            () => handler.HandleAsync(json, CancellationToken.None));
        Assert.Contains("TOKEN_EXPIRED", ex.Message);
    }

    [Fact]
    public async Task PasswordResetHandler_LockedUser_ThrowsTerminal()
    {
        var userId = Guid.NewGuid();
        var lockedUser = new ApplicationUser
        {
            Id = userId,
            Email = "locked@example.com",
            FullName = "Locked User",
            Status = UserStatus.Locked
        };

        _userManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(lockedUser);

        var handler = new PasswordResetEmailOutboxHandler(
            _sender.Object, _renderer.Object, _protector.Object,
            _userManager.Object, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            userId, "locked@example.com", "protected-token", "Locked User",
            DateTime.UtcNow.AddMinutes(10));

        var json = JsonSerializer.Serialize(payload);
        var ex = await Assert.ThrowsAsync<TerminalOutboxException>(
            () => handler.HandleAsync(json, CancellationToken.None));
        Assert.Contains("USER_INACTIVE_OR_LOCKED", ex.Message);
    }

    [Fact]
    public async Task PasswordResetHandler_ValidPayload_SendsEmail()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser
        {
            Id = userId,
            Email = "user@example.com",
            FullName = "Test User",
            Status = UserStatus.Active
        };

        _userManager.Setup(m => m.FindByIdAsync(userId.ToString()))
            .ReturnsAsync(user);
        _protector.Setup(p => p.Unprotect("protected-token")).Returns("plain-token");
        _renderer.Setup(r => r.RenderPasswordReset(
            "user@example.com", "Test User", It.IsAny<string>(), It.IsAny<TimeSpan>()))
            .Returns(new EmailMessage("user@example.com", "Subject", "<h1>HTML</h1>", "Plain"));

        var handler = new PasswordResetEmailOutboxHandler(
            _sender.Object, _renderer.Object, _protector.Object,
            _userManager.Object, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            userId, "user@example.com", "protected-token", "Test User",
            DateTime.UtcNow.AddMinutes(10));

        var json = JsonSerializer.Serialize(payload);
        await handler.HandleAsync(json, CancellationToken.None);

        _sender.Verify(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PasswordResetHandler_InvalidPayload_ThrowsTerminal()
    {
        var handler = new PasswordResetEmailOutboxHandler(
            _sender.Object, _renderer.Object, _protector.Object,
            _userManager.Object, _options,
            NullLogger<PasswordResetEmailOutboxHandler>.Instance);

        var payload = new PasswordResetPayloadDto(
            Guid.NewGuid(), "", "protected-token", null, null); // empty email

        var json = JsonSerializer.Serialize(payload);
        var ex = await Assert.ThrowsAsync<TerminalOutboxException>(
            () => handler.HandleAsync(json, CancellationToken.None));
        Assert.Contains("missing required", ex.Message);
    }

    [Fact]
    public async Task SecurityWarningHandler_UserNotFound_ThrowsTerminal()
    {
        _userManager.Setup(m => m.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var handler = new SecurityWarningEmailOutboxHandler(
            _sender.Object, _renderer.Object,
            _userManager.Object,
            NullLogger<SecurityWarningEmailOutboxHandler>.Instance);

        var payload = new UserWarningPayloadDto(
            Guid.NewGuid(), null, "Bad behavior", Guid.NewGuid());

        var json = JsonSerializer.Serialize(payload);
        var ex = await Assert.ThrowsAsync<TerminalOutboxException>(
            () => handler.HandleAsync(json, CancellationToken.None));
        Assert.Contains("USER_NOT_FOUND", ex.Message);
    }

    [Fact]
    public async Task AccountDeletionHandler_ExpiredToken_ThrowsTerminal()
    {
        var handler = new AccountDeletionEmailOutboxHandler(
            _sender.Object, _renderer.Object, _options,
            NullLogger<AccountDeletionEmailOutboxHandler>.Instance);

        var payload = new AccountDeletionEmailPayloadDto(
            Guid.NewGuid(), "test@example.com",
            "del-token", DateTime.UtcNow.AddMinutes(-10), null);

        var json = JsonSerializer.Serialize(payload);
        var ex = await Assert.ThrowsAsync<TerminalOutboxException>(
            () => handler.HandleAsync(json, CancellationToken.None));
        Assert.Contains("TOKEN_EXPIRED", ex.Message);
    }
}
