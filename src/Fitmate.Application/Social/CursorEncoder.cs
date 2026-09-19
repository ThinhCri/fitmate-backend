using Fitmate.Domain.Common;

namespace Fitmate.Application.Social;

/// <summary>
/// Cursor: base64("<unix-ms>|<guid>"). Cùng pattern cho social feed (BE-180), friend list, block list.
/// </summary>
public static class CursorEncoder
{
    public static string Encode(DateTime createdAtUtc, Guid id)
    {
        var ms = new DateTimeOffset(createdAtUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var raw = $"{ms}|{id:D}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    public static (DateTime? createdAt, Guid? id) Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return (null, null);
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2) throw new FormatException("cursor parts");
            if (!long.TryParse(parts[0], out var ms)) throw new FormatException("cursor ts");
            if (!Guid.TryParse(parts[1], out var id)) throw new FormatException("cursor id");
            return (DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, id);
        }
        catch (Exception ex) when (ex is FormatException)
        {
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "cursor invalid.",
                new Dictionary<string, string[]> { ["cursor"] = new[] { "base64(ts|guid)" } });
        }
    }
}
