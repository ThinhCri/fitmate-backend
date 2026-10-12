using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Admin;
using Fitmate.Infrastructure.Admin;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Fitmate.UnitTests;

public sealed class Ga4ReportProviderTests
{
    [Fact]
    public async Task Provider_parses_GA4_batch_response_and_caches_it()
    {
        using var rsa = RSA.Create(2048);
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["client_email"] = "test@example.iam.gserviceaccount.com",
            ["private_key"] = rsa.ExportRSAPrivateKeyPem(),
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        });
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"access_token\":\"test-token\"}") };
            request.Headers.Authorization!.Parameter.Should().Be("test-token");
            request.RequestUri.AbsolutePath.Should().Contain("properties/12345:batchRunReports");
            const string body = """
                {"reports":[
                  {"rows":[{"metricValues":[{"value":"12"},{"value":"3"},{"value":"44"},{"value":"50"}]}]},
                  {"rows":[{"dimensionValues":[{"value":"20261012"}],"metricValues":[{"value":"7"},{"value":"20"}]}]}
                ]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new Ga4ReportProvider(http, Options.Create(new Ga4Options
        {
            PropertyId = "12345",
            ServiceAccountJsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
        }), cache);
        var result = await provider.GetAsync("7d", 7, default);
        result.Configured.Should().BeTrue();
        result.ActiveUsers.Should().Be(12);
        result.NewUsers.Should().Be(3);
        result.Views.Should().Be(44);
        result.EventCount.Should().Be(50);
        result.Days.Single().Date.Should().Be(new DateOnly(2026, 10, 12));
        (await provider.GetAsync("7d", 7, default)).Should().Be(result);
        calls.Should().Be(2);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(send(request));
    }
}
