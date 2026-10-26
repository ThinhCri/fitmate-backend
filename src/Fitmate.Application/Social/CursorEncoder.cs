using Fitmate.Domain.Common;

namespace Fitmate.Application.Social;

/// <summary>
/// Opaque v2 cursor preserves timestamp ticks; accepts legacy unix-ms cursors.
/// </summary>
public static class CursorEncoder
{
    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        var raw = $"v2:{createdAtUtc.Ticks}|{id:D}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    public static (DateTime? createdAt, Guid? id) Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        try
        {
            if (cursor.Length > 256) throw new FormatException("cursor length");
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2) throw new FormatException("cursor parts");
            if (!Guid.TryParse(parts[1], out var id)) throw new FormatException("cursor id");
            var version2 = parts[0].StartsWith("v2:", StringComparison.Ordinal);
            var timestamp = version2 ? parts[0][3..] : parts[0];
            if (!long.TryParse(timestamp, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) throw new FormatException("cursor ts");
            return (version2 ? new DateTime(value, DateTimeKind.Utc) : DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime, id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "cursor invalid.",
                new Dictionary<string, string[]> { ["cursor"] = new[] { "base64(ts|guid)" } });
        }
    }
}
