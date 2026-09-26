using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Fitmate.Infrastructure.Payments.Payos;

// payOS payment-requests signing scheme (NOT the payouts signing scheme).
public static class PayosSignature
{
    public static string Sign(string canonical, string key) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

    public static bool Verify(JsonElement data, string? signature, string key)
    {
        if (data.ValueKind != JsonValueKind.Object || signature?.Length != 64 || string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            var fields = data.EnumerateObject().ToArray();
            if (fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length) return false;
            var canonical = string.Join("&", fields.OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => p.Name + "=" + Value(p.Value)));
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sign(canonical, key)), Convert.FromHexString(signature));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException) { return false; }
    }

    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "",
        JsonValueKind.String => value.GetString() is "null" or "undefined" ? "" : value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => JsonSerializer.Serialize(value.EnumerateArray().Select(v =>
            v.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).ToDictionary(p => p.Name, p => p.Value)),
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
        _ => throw new JsonException("Unsupported payment signature field.")
    };
}
