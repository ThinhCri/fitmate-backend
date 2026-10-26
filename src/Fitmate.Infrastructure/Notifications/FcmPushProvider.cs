using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fitmate.Application.Notifications;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>Real FCM HTTP v1 transport. Never forward private inbox text to the lock screen.</summary>
public sealed class FcmPushProvider(HttpClient http, IOptions<FcmPushOptions> options, IMemoryCache cache) : IPushProvider
{
    private const string TokenUri = "https://oauth2.googleapis.com/token";
    public static bool ValidConfiguration(FcmPushOptions value)
    {
        if (!value.Enabled) return true;
        try { ReadCredentials(value); return true; }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException or CryptographicException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    private static (string Email, string Key) ReadCredentials(FcmPushOptions value)
    {
        if (string.IsNullOrWhiteSpace(value.ProjectId) || value.ProjectId.Length > 100 ||
            value.ProjectId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'))) throw new FormatException();
        using var json = JsonDocument.Parse(Convert.FromBase64String(value.ServiceAccountJsonBase64));
        var root = json.RootElement;
        if (root.GetProperty("type").GetString() != "service_account" ||
            root.GetProperty("project_id").GetString() != value.ProjectId ||
            root.GetProperty("token_uri").GetString() != TokenUri) throw new FormatException();
        var email = root.GetProperty("client_email").GetString();
        var key = root.GetProperty("private_key").GetString();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(key)) throw new FormatException();
        using var rsa = RSA.Create(); rsa.ImportFromPem(key);
        return (email, key);
    }

    public async Task<PushSendResult> SendAsync(string decryptedToken, string title, string? summary, string payloadJson, CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || !ValidConfiguration(settings)) return PushSendResult.ProviderNotConfigured("fcm_not_configured");
        try
        {
            var credentials = ReadCredentials(settings);
            var cacheKey = $"fcm-oauth:{settings.ProjectId}:{credentials.Email}";
            var accessToken = await cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(45);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var unsigned = Encode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" })) + "." +
                    Encode(JsonSerializer.Serialize(new { iss = credentials.Email, scope = "https://www.googleapis.com/auth/firebase.messaging", aud = TokenUri, iat = now, exp = now + 3600 }));
                using var rsa = RSA.Create(); rsa.ImportFromPem(credentials.Key);
                var assertion = unsigned + "." + Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                using var tokenResponse = await http.PostAsync(TokenUri, new FormUrlEncodedContent(new Dictionary<string, string> {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = assertion }), ct);
                if (!tokenResponse.IsSuccessStatusCode) throw new HttpRequestException("fcm_oauth_unavailable");
                using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
                var token = tokenJson.RootElement.GetProperty("access_token").GetString();
                return string.IsNullOrWhiteSpace(token) ? throw new HttpRequestException("fcm_oauth_invalid") : token;
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{settings.ProjectId}/messages:send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new { message = new {
                token = decryptedToken,
                notification = new { title = "Fitmate", body = "Bạn có thông báo mới. Mở Fitmate để xem." },
                data = new { screen = "notifications" },
                android = new { priority = "normal", ttl = "60s" }
            } });
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return PushSendResult.Success();
            if (response.StatusCode == HttpStatusCode.Unauthorized) cache.Remove(cacheKey);
            // A generic INVALID_ARGUMENT can mean a malformed payload, not an invalid registration token.
            var unregistered = false;
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (error.RootElement.TryGetProperty("error", out var detail) && detail.TryGetProperty("details", out var details))
                    unregistered = details.EnumerateArray().Any(d => d.TryGetProperty("errorCode", out var code) && code.GetString() == "UNREGISTERED");
            }
            catch (JsonException) { }
            if (unregistered) return PushSendResult.TokenInvalid("fcm_unregistered");
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.Unauthorized)
                return PushSendResult.Retryable("fcm_transient_failure");
            if (response.StatusCode == HttpStatusCode.Forbidden) return PushSendResult.ProviderNotConfigured("fcm_permission_denied");
            return PushSendResult.PayloadRejected("fcm_request_rejected");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return PushSendResult.Retryable("fcm_transport_failure");
        }
    }
    private static string Encode(string value) => Base64Url(Encoding.UTF8.GetBytes(value));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
