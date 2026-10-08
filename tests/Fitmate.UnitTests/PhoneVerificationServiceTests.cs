using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using FluentAssertions;
using Moq;
using Xunit;

namespace Fitmate.UnitTests;

public sealed class PhoneVerificationServiceTests
{
    private sealed class Gateway : IPhoneVerificationGateway
    {
        public int Starts { get; private set; }
        public bool Accepted { get; set; }
        public Task<PhoneVerificationStartResult> StartAsync(Guid userId, string phone, CancellationToken ct)
        {
            Starts++;
            return Task.FromResult(new PhoneVerificationStartResult(DateTime.UtcNow.AddMinutes(5), 60));
        }
        public Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken ct) => Task.FromResult(Accepted);
    }

    private sealed class Sender(bool configured) : ISmsOtpSender
    {
        public bool IsConfigured => configured;
        public Task SendAsync(string phoneNumberE164, string code, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task Start_UnconfiguredProvider_DoesNotIssueChallenge()
    {
        var gateway = new Gateway();
        var service = new PhoneVerificationService(AlwaysAllowAuthz.Instance, gateway, new Sender(false));

        var action = () => service.StartAsync(Guid.NewGuid(), "+84901234567", CancellationToken.None);

        await action.Should().ThrowAsync<DomainException>().Where(e => e.HttpStatus == 503);
        gateway.Starts.Should().Be(0);
    }

    [Theory]
    [InlineData("0901234567")]
    [InlineData("+84901234")]
    [InlineData("+8490123456789")]
    public async Task Start_InvalidNumber_DoesNotIssueChallenge(string phone)
    {
        var gateway = new Gateway();
        var service = new PhoneVerificationService(AlwaysAllowAuthz.Instance, gateway, new Sender(true));

        await FluentActions.Invoking(() => service.StartAsync(Guid.NewGuid(), phone, CancellationToken.None))
            .Should().ThrowAsync<ValidationException>();
        gateway.Starts.Should().Be(0);
    }

    [Fact]
    public async Task Confirm_InvalidCode_IsRejected()
    {
        var service = new PhoneVerificationService(AlwaysAllowAuthz.Instance, new Gateway(), new Sender(true));

        await FluentActions.Invoking(() => service.ConfirmAsync(Guid.NewGuid(), "12345", CancellationToken.None))
            .Should().ThrowAsync<ValidationException>();
    }
}
