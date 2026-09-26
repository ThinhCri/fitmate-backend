using System.Text.RegularExpressions;

namespace Fitmate.Api.Observability;

/// <summary>
/// BE-824: Log Redactor nhằm ngăn chặn việc rò rỉ dữ liệu nhạy cảm (PII, credentials, health info, chat) vào log sinks.
/// </summary>
public static class LogRedactor
{
    private const string Mask = "[REDACTED]";

    // Regex bắt các trường nhạy cảm trong JSON hoặc key-value: password, token, secret, vnp_SecureHash
    private static readonly Regex SensitiveKeyPattern = new(
        @"""(?i:(password|confirm_?password|new_?password|current_?password|access_?token|refresh_?token|token|vnp_?securehash|secret|api_?key|private_?key))""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    // Regex bắt Bearer token trong chuỗi authorization
    private static readonly Regex BearerTokenPattern = new(
        @"(?i:bearer\s+)([a-zA-Z0-9_\-\.]+)",
        RegexOptions.Compiled);

    // Regex bắt thẻ tín dụng (13-19 chữ số)
    private static readonly Regex CreditCardPattern = new(
        @"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|6(?:011|5[0-9]{2})[0-9]{12})\b",
        RegexOptions.Compiled);

    // Danh sách các key nhạy cảm khi filter dictionary hoặc query parameters
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "confirmPassword",
        "newPassword",
        "currentPassword",
        "token",
        "refreshToken",
        "accessToken",
        "vnp_SecureHash",
        "secret",
        "client_secret",
        "authorization",
        "X-Finance-Totp",
        "x-api-key",
        "checksumKey",
        "signature",
        "accountNumber",
        "medicalHistory",
        "medical_history",
        "allergies",
        "raw_chat",
        "chat_message",
        "message_body"
    };

    /// <summary>
    /// Làm sạch chuỗi raw JSON hoặc log message bằng regex.
    /// </summary>
    public static string RedactText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;

        // 1. Redact key-value pairs
        var redacted = SensitiveKeyPattern.Replace(input, @"""$1"": """ + Mask + @"""");

        // 2. Redact Bearer tokens
        redacted = BearerTokenPattern.Replace(redacted, "Bearer " + Mask);

        // 3. Redact Credit card numbers
        redacted = CreditCardPattern.Replace(redacted, Mask);

        return redacted;
    }

    /// <summary>
    /// Kiểm tra một key tên thuộc tính có phải là dữ liệu nhạy cảm hay không.
    /// </summary>
    public static bool IsSensitiveKey(string key)
    {
        return SensitiveKeys.Contains(key) ||
               key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("secret", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Làm sạch một dictionary chứa các thuộc tính log metadata.
    /// </summary>
    public static Dictionary<string, object?> RedactDictionary(IDictionary<string, object?>? dictionary)
    {
        if (dictionary == null) return new Dictionary<string, object?>();

        var result = new Dictionary<string, object?>();
        foreach (var (k, v) in dictionary)
        {
            if (IsSensitiveKey(k))
            {
                result[k] = Mask;
            }
            else if (v is string s)
            {
                result[k] = RedactText(s);
            }
            else
            {
                result[k] = v;
            }
        }
        return result;
    }
}
