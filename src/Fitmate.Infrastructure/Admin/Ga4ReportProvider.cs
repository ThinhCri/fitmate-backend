using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Admin;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Admin;

public sealed class Ga4ReportProvider(
    HttpClient http, IOptions<Ga4Options> settings, IMemoryCache cache) : IGa4ReportProvider
{
    private const string Scope = "https://www.googleapis.com/auth/analytics.readonly";
    public bool IsConfigured
    {
        get
        {
            var options = settings.Value;
            var hasId = !string.IsNullOrWhiteSpace(options.PropertyId);
            var hasKey = !string.IsNullOrWhiteSpace(options.ServiceAccountJsonBase64);
            if (hasId != hasKey) throw Unavailable();
            return hasId && hasKey;
        }
    }

    public Task<AnalyticsOverview> GetAsync(string range, int days, CancellationToken ct)
    {
        var propertyId = settings.Value.PropertyId.Trim();
        if (!IsConfigured || !propertyId.All(char.IsDigit)) throw Unavailable();
        return cache.GetOrCreateAsync($"ga4:{propertyId}:{range}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await FetchAsync(propertyId, range, days, ct);
        })!;
    }

    private async Task<AnalyticsOverview> FetchAsync(string propertyId, string range, int days, CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);
        var dateRanges = new[] { new { startDate = $"{days - 1}daysAgo", endDate = "today" } };
        var requests = new object[]
        {
            new { dateRanges, metrics = new[] { new { name = "activeUsers" }, new { name = "newUsers" },
                new { name = "screenPageViews" }, new { name = "eventCount" } } },
            new { dateRanges, metrics = new[] { new { name = "activeUsers" }, new { name = "screenPageViews" } },
                dimensions = new[] { new { name = "date" } }, limit = "100" }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://analyticsdata.googleapis.com/v1beta/properties/{propertyId}:batchRunReports")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { requests }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var reports = document.RootElement.GetProperty("reports");
            if (reports.ValueKind != JsonValueKind.Array || reports.GetArrayLength() != 2)
                throw Unavailable();
            var totals = reports[0];
            var values = totals.TryGetProperty("rows", out var rows) && rows.GetArrayLength() > 0
                ? rows[0].GetProperty("metricValues") : default;
            long Number(JsonElement array, int index) =>
                array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > index &&
                long.TryParse(array[index].GetProperty("value").GetString(), out var value) ? value : 0;
            var daily = new List<AnalyticsDay>();
            if (reports[1].TryGetProperty("rows", out var dailyRows))
                foreach (var row in dailyRows.EnumerateArray())
                {
                    var date = row.GetProperty("dimensionValues")[0].GetProperty("value").GetString();
                    if (!DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var parsed)) continue;
                    var metrics = row.GetProperty("metricValues");
                    daily.Add(new AnalyticsDay(parsed, Number(metrics, 0), Number(metrics, 1)));
                }
            return new AnalyticsOverview(true, range, Number(values, 0), Number(values, 1),
                Number(values, 2), Number(values, 3), daily.OrderBy(d => d.Date).ToArray());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw Unavailable();
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(settings.Value.ServiceAccountJsonBase64));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var email = root.GetProperty("client_email").GetString();
            var key = root.GetProperty("private_key").GetString();
            var tokenUri = root.GetProperty("token_uri").GetString();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key) ||
                tokenUri != "https://oauth2.googleapis.com/token") throw Unavailable();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
            var payload = Base64Url(JsonSerializer.Serialize(new
            {
                iss = email, scope = Scope, aud = tokenUri, iat = now, exp = now + 3600
            }));
            var signedInput = header + "." + payload;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key);
            var signature = Convert.ToBase64String(rsa.SignData(Encoding.ASCII.GetBytes(signedInput),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = signedInput + "." + signature
                })
            };
            using var response = await SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var token = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return token.RootElement.GetProperty("access_token").GetString() ?? throw Unavailable();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        {
            throw Unavailable();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private static string Base64Url(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static DomainException Unavailable() => new("GA4_UNAVAILABLE",
        "Google Analytics chưa được cấu hình hoặc tạm thời không thể tải dữ liệu.", 503);
}
