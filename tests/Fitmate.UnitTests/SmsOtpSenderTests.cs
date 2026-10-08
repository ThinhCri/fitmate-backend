using Fitmate.Application.Auth;
using Fitmate.Infrastructure.Auth;
using FluentAssertions;
using Xunit;

namespace Fitmate.UnitTests;

public sealed class SmsOtpSenderTests
{
    [Fact]
    public async Task UnconfiguredAdapter_FailsClosedWithoutSending()
    {
        ISmsOtpSender sender = new UnconfiguredSmsOtpSender();

        var action = () => sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SMS_PROVIDER_NOT_CONFIGURED");
    }
}
