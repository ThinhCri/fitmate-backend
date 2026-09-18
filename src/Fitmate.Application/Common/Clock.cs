using Fitmate.Domain.Common;

namespace Fitmate.Application.Common;

/// <summary>
/// Time/UTC helpers. Luôn UTC ở DB; convert sang local theo user timezone khi trả FE.
/// </summary>
public static class Clock
{
    public static DateTime UtcNow => DateTime.UtcNow;
    public static DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    public static DateTime ToUtc(DateTime local, string timezoneId)
    {
        var localUnspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localUnspecified, ResolveTimezone(timezoneId));
    }

    public static DateTime ToLocal(DateTime utc, string timezoneId)
    {
        var utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcKind, ResolveTimezone(timezoneId));
    }

    private static TimeZoneInfo ResolveTimezone(string tz)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
    }
}
