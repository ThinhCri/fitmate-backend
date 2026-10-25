using System.Text.Json;
using Fitmate.Application.Email;
using Fitmate.Application.Identity;
using Fitmate.Application.Outbox;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public sealed class DeletionOtpEmailTests
{
    [Fact]
    public async Task Handler_DecryptsOnlyAtDelivery()
    {
        var sender = new Mock<IEmailSender>();
        var protector = new InMemoryDeletionTokenProtector();
        var payload = new DeletionOtpEmailPayload(protector.Protect("test@example.test"), protector.Protect("123456"), DateTime.UtcNow.AddMinutes(5));
        var handler = new DeletionOtpEmailOutboxHandler(sender.Object, protector);
        await handler.HandleAsync(JsonSerializer.Serialize(payload), default);
        sender.Verify(s => s.SendAsync(It.Is<EmailMessage>(m => m.ToEmail == "test@example.test" &&
            m.PlainTextBody.Contains("123456") && m.Subject.Contains("xóa tài khoản")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("123456", -1)]
    [InlineData("<bad>", 5)]
    public async Task Handler_DoesNotSendExpiredOrMalformedCode(string code, int minutes)
    {
        var sender = new Mock<IEmailSender>();
        var protector = new InMemoryDeletionTokenProtector();
        var payload = new DeletionOtpEmailPayload(protector.Protect("test@example.test"), protector.Protect(code), DateTime.UtcNow.AddMinutes(minutes));
        var handler = new DeletionOtpEmailOutboxHandler(sender.Object, protector);
        var act = () => handler.HandleAsync(JsonSerializer.Serialize(payload), default);
        await act.Should().ThrowAsync<TerminalOutboxException>();
        sender.Verify(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
