using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fitmate.Application.Idempotency;

public static class CanonicalJsonHasher
{
    public static string ComputeHash(byte[] body, string? contentType)
    {
        var normalizedContentType = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        bool isJson = normalizedContentType.StartsWith("application/json", StringComparison.Ordinal)
                   || normalizedContentType.EndsWith("+json", StringComparison.Ordinal);

        if (isJson)
        {
            if (body.Length == 0)
            {
                return HashBytes(Encoding.UTF8.GetBytes(normalizedContentType + "\n"));
            }

            // Must throw JsonException on malformed JSON so caller can return validation error
            using var doc = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                WriteCanonicalElement(doc.RootElement, writer);
            }

            var canonicalBody = ms.ToArray();
            var prefix = Encoding.UTF8.GetBytes(normalizedContentType + "\n");
            var combined = new byte[prefix.Length + canonicalBody.Length];
            Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
            Buffer.BlockCopy(canonicalBody, 0, combined, prefix.Length, canonicalBody.Length);
            return HashBytes(combined);
        }

        // Non-JSON content types: hash normalized content type + raw bytes
        var p = Encoding.UTF8.GetBytes(normalizedContentType + "\n");
        var c = new byte[p.Length + body.Length];
        Buffer.BlockCopy(p, 0, c, 0, p.Length);
        Buffer.BlockCopy(body, 0, c, p.Length, body.Length);
        return HashBytes(c);
    }

    private static void WriteCanonicalElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var props = element.EnumerateObject().ToList();
                props.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                foreach (var prop in props)
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonicalElement(prop.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalElement(item, writer);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                    writer.WriteNumberValue(l);
                else if (element.TryGetDecimal(out var d))
                    writer.WriteNumberValue(d);
                else if (element.TryGetDouble(out var dbl))
                    writer.WriteNumberValue(dbl);
                else
                    writer.WriteRawValue(element.GetRawText());
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            case JsonValueKind.Undefined:
            default:
                break;
        }
    }

    private static string HashBytes(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
