using Fitmate.Application.Auth;
using Fitmate.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

    [Fact]
    public async Task SpeedSms_SendsOtpWithBasicAuthAndNoTokenInUrl()
    {
        var handler = new StubHandler("""{"status":"success","code":"00","data":{"invalidPhone":[]}}""");
        var sender = CreateSender(handler);

        await sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        handler.RequestUri.Should().Be("https://api.speedsms.vn/index.php/sms/send");
        handler.Authorization.Should().Be(new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("test-token:x"))));
        handler.Body.Should().Contain("0901234567").And.Contain("123456");
        handler.Body.Should().Contain("\"type\":4");
    }

    [Fact]
    public async Task SpeedSms_SendsCustomerCareOtpWithoutBrandname()
    {
        var handler = new StubHandler("""{"status":"success","code":"00","data":{"invalidPhone":[]}}""");
        var sender = CreateSender(handler, smsType: 2);

        sender.IsConfigured.Should().BeTrue();
        await sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        handler.Body.Should().Contain("\"to\":[\"0901234567\"]")
            .And.Contain("\"type\":2").And.Contain("\"sender\":\"\"");
    }

    [Fact]
    public async Task SpeedSms_RejectsProviderErrorsWithoutLeakingResponse()
    {
        var handler = new StubHandler("""{"status":"error","code":"105","message":"phone 84901234567 invalid"}""");
        var sender = CreateSender(handler);

        var action = () => sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        await action.Should().ThrowAsync<Fitmate.Application.Outbox.TerminalOutboxException>()
            .WithMessage("SMS_PROVIDER_REJECTED_REQUEST");
    }

    [Theory]
    [InlineData("300", "SMS_PROVIDER_INSUFFICIENT_BALANCE")]
    [InlineData("009", "SMS_PROVIDER_ACCESS_DENIED_009")]
    public async Task SpeedSms_IdentifiesPermanentAccountErrorsWithoutLeakingResponse(string code, string expected)
    {
        var handler = new StubHandler($$"""{"status":"error","code":"{{code}}","message":"secret phone 84901234567"}""");
        var sender = CreateSender(handler);

        var action = () => sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        await action.Should().ThrowAsync<Fitmate.Application.Outbox.TerminalOutboxException>()
            .WithMessage(expected);
    }

    [Fact]
    public async Task SpeedSms_RetainsOnlyNumericTransientProviderCode()
    {
        var handler = new StubHandler("""{"status":"error","code":"500","message":"secret phone 84901234567"}""");
        var sender = CreateSender(handler);

        var action = () => sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SMS_PROVIDER_UNAVAILABLE_500");
    }

    [Fact]
    public async Task SpeedSms_ReportsOnlySafeResponseShapeWhenCodeIsMissing()
    {
        var handler = new StubHandler("""{"status":"error","message":"secret phone 84901234567"}""");
        var sender = CreateSender(handler);

        var action = () => sender.SendAsync("+84901234567", "123456", CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SMS_PROVIDER_UNAVAILABLE_STATUS_ERROR_CODE_MISSING");
    }

    [Fact]
    public async Task SpeedSms_WithoutToken_DoesNotCallProvider()
    {
        var handler = new StubHandler("""{"status":"success","code":"00"}""");
        var sender = CreateSender(handler, string.Empty);

        sender.IsConfigured.Should().BeFalse();
        var action = () => sender.SendAsync("+84901234567", "123456", CancellationToken.None);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("SMS_PROVIDER_NOT_CONFIGURED");
        handler.RequestUri.Should().BeNull();
    }

    private static SpeedSmsOtpSender CreateSender(StubHandler handler, string token = "test-token", int smsType = 4) =>
        new(new HttpClient(handler), Options.Create(new SpeedSmsOptions
        {
            Provider = "SpeedSms", AccessToken = token, SmsType = smsType
        }));

    private sealed class StubHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseBody) };
        }
    }
}
