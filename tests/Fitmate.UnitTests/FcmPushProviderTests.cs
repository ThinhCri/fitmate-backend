using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Notifications;
using Fitmate.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

public sealed class FcmPushProviderTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private static FcmPushOptions Configuration()
    {
        using var rsa = RSA.Create(2048);
        return new FcmPushOptions { Enabled = true, ProjectId = "test-project", ServiceAccountJsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            type = "service_account", project_id = "test-project", client_email = "push@test-project.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(), token_uri = "https://oauth2.googleapis.com/token"
        }))) };
    }

    [Fact]
    public async Task RealTransport_UsesOAuthAndNeverForwardsPrivateInboxContent()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(async request => {
            calls++;
            if (calls == 1)
            {
                request.RequestUri!.AbsoluteUri.Should().Be("https://oauth2.googleapis.com/token");
                (await request.Content!.ReadAsStringAsync()).Should().Contain("assertion=");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"unit-test-token\"}") };
            }
            request.RequestUri!.AbsoluteUri.Should().Be("https://fcm.googleapis.com/v1/projects/test-project/messages:send");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            var body = await request.Content!.ReadAsStringAsync();
            body.Should().NotContain("private medical title").And.NotContain("private summary").And.NotContain("private-payload");
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("message").GetProperty("data").GetProperty("screen").GetString().Should().Be("notifications");
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await new FcmPushProvider(http, Options.Create(Configuration()), cache)
            .SendAsync("device-token", "private medical title", "private summary", "private-payload");
        result.Outcome.Should().Be(PushSendOutcome.Success); calls.Should().Be(2);
    }

    [Theory]
    [InlineData(400, "{}", PushSendOutcome.PayloadRejected)]
    [InlineData(404, "{\"error\":{\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}", PushSendOutcome.TokenInvalid)]
    [InlineData(429, "{}", PushSendOutcome.Retryable)]
    [InlineData(503, "{}", PushSendOutcome.Retryable)]
    [InlineData(403, "{}", PushSendOutcome.ProviderNotConfigured)]
    public async Task ProviderFailures_AreClassifiedWithoutReturningRawErrors(int status, string body, PushSendOutcome expected)
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.Host == "oauth2.googleapis.com"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"unit-test-token\"}") }
            : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) })));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await new FcmPushProvider(http, Options.Create(Configuration()), cache).SendAsync("device-token", "title", null, "{}");
        result.Outcome.Should().Be(expected); result.ProviderMessage.Should().StartWith("fcm_");
    }

    [Fact]
    public async Task MissingConfiguration_DoesNotCallProvider()
    {
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not call network")));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await new FcmPushProvider(http, Options.Create(new FcmPushOptions()), cache).SendAsync("device", "title", null, "{}");
        result.Outcome.Should().Be(PushSendOutcome.ProviderNotConfigured);
        FcmPushProvider.ValidConfiguration(new FcmPushOptions { Enabled = true }).Should().BeFalse();
    }
}
