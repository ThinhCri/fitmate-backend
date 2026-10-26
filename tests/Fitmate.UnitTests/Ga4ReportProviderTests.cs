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
            if (request.RequestUri.AbsolutePath.EndsWith(":runRealtimeReport"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"rows\":[{\"metricValues\":[{\"value\":\"2\"},{\"value\":\"5\"}]}]}") };
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
        result.RealtimeActiveUsers.Should().Be(2);
        result.RealtimeViews.Should().Be(5);
        calls.Should().Be(4);
    }

    [Fact]
    public async Task Sources_use_separate_filters_and_cache_entries_for_both_report_types()
    {
        using var rsa = RSA.Create(2048);
        var account = JsonSerializer.Serialize(new { client_email = "test@example.iam.gserviceaccount.com",
            private_key = rsa.ExportRSAPrivateKeyPem(), token_uri = "https://oauth2.googleapis.com/token" });
        var bodies = new List<(string Path, string Body)>();
        using var http = new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"test-token\"}") };
            var body = await request.Content!.ReadAsStringAsync();
            bodies.Add((request.RequestUri.AbsolutePath, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri.AbsolutePath.EndsWith(":runRealtimeReport")
                    ? "{\"rows\":[]}" : "{\"reports\":[{},{}]}") };
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new Ga4ReportProvider(http, Options.Create(new Ga4Options {
            PropertyId = "12345", LandingStreamId = "67890",
            ServiceAccountJsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(account))
        }), cache);

        var app = await provider.GetAsync("7d", 7, default, "app");
        var landing = await provider.GetAsync("7d", 7, default, "landing");
        app.Source.Should().Be("app");
        landing.Source.Should().Be("landing");
        landing.RealtimeStatus.Should().Be("available");
        landing.RealtimeActiveUsers.Should().Be(0);
        await provider.GetAsync("7d", 7, default, "app");
        await provider.GetAsync("7d", 7, default, "landing");
        bodies.Should().HaveCount(4);
        foreach (var (path, body) in bodies)
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var reports = path.EndsWith(":batchRunReports") ? root.GetProperty("requests").EnumerateArray().ToArray() : [root];
            foreach (var report in reports)
            {
                var filter = report.GetProperty("dimensionFilter");
                if (!filter.TryGetProperty("andGroup", out var group))
                {
                    filter.GetProperty("filter").GetProperty("fieldName").GetString().Should().Be("platform");
                    filter.GetRawText().Should().Contain("Android").And.Contain("iOS");
                }
                else
                {
                    group.GetRawText().Should().Contain("web");
                    var match = group.GetProperty("expressions")[1].GetProperty("filter");
                    if (path.EndsWith(":runRealtimeReport"))
                    {
                        match.GetProperty("fieldName").GetString().Should().Be("streamId");
                        match.GetRawText().Should().Contain("67890").And.NotContain("hostName");
                    }
                    else
                    {
                        match.GetProperty("fieldName").GetString().Should().Be("hostName");
                        match.GetRawText().Should().Contain("fitmate.site").And.Contain("www.fitmate.site");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(false, "unavailable")]
    [InlineData(true, "not_configured")]
    public async Task Historical_metrics_survive_realtime_failure_or_missing_landing_stream(bool missingStream, string expected)
    {
        using var rsa = RSA.Create(2048);
        var account = JsonSerializer.Serialize(new { client_email = "test@example.iam.gserviceaccount.com",
            private_key = rsa.ExportRSAPrivateKeyPem(), token_uri = "https://oauth2.googleapis.com/token" });
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"test-token\"}") };
            if (request.RequestUri.AbsolutePath.EndsWith(":runRealtimeReport"))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"reports\":[{},{}]}") };
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new Ga4ReportProvider(http, Options.Create(new Ga4Options { PropertyId = "12345",
            LandingStreamId = missingStream ? "" : "67890",
            ServiceAccountJsonBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(account)) }), cache);
        var result = await provider.GetAsync("7d", 7, default, "landing");
        result.Configured.Should().BeTrue();
        result.Views.Should().Be(0);
        result.RealtimeStatus.Should().Be(expected);
        result.RealtimeActiveUsers.Should().BeNull();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(send(request));
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
}
