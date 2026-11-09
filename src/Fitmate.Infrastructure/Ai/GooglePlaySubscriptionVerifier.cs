using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Ai;
using Fitmate.Domain.Common;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Ai;

public sealed class GooglePlaySubscriptionVerifier(HttpClient http, IOptions<GooglePlayBillingOptions> settings)
    : IGooglePlaySubscriptionVerifier, IGooglePlayOrderReader, IGooglePlayOrderDiscovery
{
    private const string Scope = "https://www.googleapis.com/auth/androidpublisher";

    public async Task<VerifiedGoogleSubscription> VerifyAsync(string purchaseToken, CancellationToken ct)
    {
        var config = settings.Value;
        if (!config.Enabled) throw Unavailable();
        var accessToken = await GetAccessTokenAsync(ct);
        var uri = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(config.PackageName)}/purchases/subscriptionsv2/tokens/{Uri.EscapeDataString(purchaseToken)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendGoogleRequestAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.Gone)
            throw new DomainException("AI_SUBSCRIPTION_INVALID", "Google Play không xác nhận giao dịch này.", 400);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var state = ReadString(root, "subscriptionState") ?? "SUBSCRIPTION_STATE_UNSPECIFIED";
        var acknowledged = root.TryGetProperty("acknowledgementState", out var ack) &&
            ack.GetString() == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED";
        var linked = ReadString(root, "linkedPurchaseToken");
        if (!root.TryGetProperty("lineItems", out var lineItems) || lineItems.ValueKind != JsonValueKind.Array)
            throw new DomainException("AI_SUBSCRIPTION_INVALID", "Giao dịch Google Play không chứa gói hợp lệ.", 400);
        DateTime? expiry = null;
        var matched = false;
        string? orderId = null;
        foreach (var item in lineItems.EnumerateArray())
        {
            if (ReadString(item, "productId") != config.ProductId) continue;
            if (!item.TryGetProperty("offerDetails", out var offer) ||
                ReadString(offer, "basePlanId") != config.BasePlanId) continue;
            // AI Plus is sold only as a non-renewing prepaid Play base plan.
            // Matching the base plan ID alone does not prove its billing type.
            if (!item.TryGetProperty("prepaidPlan", out var prepaidPlan) ||
                prepaidPlan.ValueKind != JsonValueKind.Object ||
                item.TryGetProperty("autoRenewingPlan", out var autoRenewingPlan) &&
                autoRenewingPlan.ValueKind != JsonValueKind.Null)
                throw new DomainException("AI_SUBSCRIPTION_WRONG_PLAN_TYPE",
                    "Gói AI Plus phải là gói trả trước, không tự gia hạn.", 400);
            matched = true;
            orderId = ReadString(item, "latestSuccessfulOrderId") ?? orderId;
            if (DateTime.TryParse(ReadString(item, "expiryTime"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed) &&
                (!expiry.HasValue || parsed > expiry.Value)) expiry = parsed.ToUniversalTime();
        }
        if (!matched) throw new DomainException("AI_SUBSCRIPTION_WRONG_PRODUCT", "Giao dịch không thuộc gói AI Plus của Fitmate.", 400);
        return new VerifiedGoogleSubscription(config.ProductId, config.BasePlanId, state, expiry, linked, acknowledged,
            root.TryGetProperty("testPurchase", out var test) && test.ValueKind != JsonValueKind.Null,
            orderId ?? ReadString(root, "latestOrderId"),
            root.TryGetProperty("externalAccountIdentifiers", out var identifiers)
                ? ReadString(identifiers, "obfuscatedExternalAccountId") : null);
    }

    public async Task<VerifiedPlayOrder> GetOrderAsync(string orderId, string purchaseToken, CancellationToken ct)
    {
        if (!settings.Value.Enabled) throw Unavailable();
        var accessToken = await GetAccessTokenAsync(ct);
        var uri = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(settings.Value.PackageName)}/orders/{Uri.EscapeDataString(orderId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendGoogleRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return GooglePlayOrderParser.Parse(document.RootElement, orderId, purchaseToken, settings.Value.ProductId, settings.Value.BasePlanId);
    }

    public async Task<string> GetPurchaseTokenAsync(string orderId, CancellationToken ct)
    {
        if (!settings.Value.Enabled || !System.Text.RegularExpressions.Regex.IsMatch(orderId,
                @"\AGPA\.[0-9]{4}-[0-9]{4}-[0-9]{4}-[0-9]{5}(?:\.\.[0-9]+)?\z"))
            throw new DomainException("VALIDATION_ERROR", "Mã đơn Google Play không hợp lệ.", 400);
        var accessToken = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(settings.Value.PackageName)}/orders/{Uri.EscapeDataString(orderId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendGoogleRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw Unavailable();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = ReadString(document.RootElement, "purchaseToken");
        if (string.IsNullOrWhiteSpace(token)) throw Unavailable();
        _ = GooglePlayOrderParser.Parse(document.RootElement, orderId, token, settings.Value.ProductId, settings.Value.BasePlanId);
        return token;
    }

    public async Task AcknowledgeAsync(string purchaseToken, CancellationToken ct)
    {
        var config = settings.Value;
        var accessToken = await GetAccessTokenAsync(ct);
        var uri = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(config.PackageName)}/purchases/subscriptions/{Uri.EscapeDataString(config.ProductId)}/tokens/{Uri.EscapeDataString(purchaseToken)}:acknowledge";
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendGoogleRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw Unavailable();
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(settings.Value.ServiceAccountJsonBase64));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var email = ReadString(root, "client_email");
            var key = ReadString(root, "private_key");
            var tokenUri = ReadString(root, "token_uri");
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
            using var response = await SendGoogleRequestAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var token = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return ReadString(token.RootElement, "access_token") ?? throw Unavailable();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            throw Unavailable();
        }
    }

    private static string Base64Url(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private async Task<HttpResponseMessage> SendGoogleRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw Unavailable();
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Unavailable();
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DomainException Unavailable() => new("AI_BILLING_UNAVAILABLE",
        "Chưa thể kiểm tra giao dịch Google Play. Vui lòng thử lại sau.", 503);
}
