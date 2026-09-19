namespace Fitmate.Application.Common;

/// <summary>
/// Strongly-typed config binding. Đọc từ appsettings hoặc env var qua IConfiguration.
/// </summary>
public class AppOptions
{
    public JwtOptions Jwt { get; set; } = new();
    public MoneyOptions Money { get; set; } = new();
    public BookingOptions Booking { get; set; } = new();
    public AiOptions Ai { get; set; } = new();
    public CorsOptions Cors { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
    public PlatformPolicyOptions PlatformPolicy { get; set; } = new();
}

public class JwtOptions
{
    public string SigningKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "fitmate-api";
    public string Audience { get; set; } = "fitmate-clients";
    public int AccessTokenTtlSeconds { get; set; } = 900;
    public int RefreshTokenTtlSeconds { get; set; } = 2592000;
}

public class MoneyOptions
{
    public decimal PlatformFeeRate { get; set; } = 0.10m;
    public string PolicyVersion { get; set; } = "v1";
}

public class BookingOptions
{
    public int HoldTtlMinutes { get; set; } = 5;
    public int CancelDeadlineHours { get; set; } = 24; // TODO: chốt trong domain-rules
}

public class AiOptions
{
    public bool Enabled { get; set; } = false;
    public string Provider { get; set; } = "stub";
    public int TimeoutMs { get; set; } = 8000;
    public int StubLatencyMs { get; set; } = 200;
}

public class CorsOptions
{
    public List<string> AllowedOrigins { get; set; } = new();
}

public class RateLimitOptions
{
    public int PerMinutePerIp { get; set; } = 60;
    public int AuthPerMinutePerIp { get; set; } = 5;
}
