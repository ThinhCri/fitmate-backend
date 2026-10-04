namespace Fitmate.Api;

/// <summary>
/// Helper nạp biến môi trường từ file .env cục bộ khi chạy trực tiếp bằng dotnet run.
/// Giúp local development thuận tiện mà không ghi đè biến đã cấu hình sẵn trong container/CI.
/// </summary>
internal static class DotEnvLoader
{
    public static void Load()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".env"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env"),
            Path.Combine(AppContext.BaseDirectory, ".env"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
                    var idx = trimmed.IndexOf('=');
                    if (idx > 0)
                    {
                        var key = trimmed.Substring(0, idx).Trim();
                        var val = trimmed.Substring(idx + 1).Trim();
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                        {
                            Environment.SetEnvironmentVariable(key, val);
                        }
                    }
                }
                break;
            }
        }
    }
}
