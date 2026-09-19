namespace Fitmate.Application.Common;

/// <summary>
/// BE-151: Read-only platform policy configuration (DEC-029).
/// Bound từ env vars / appsettings qua <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// </summary>
/// <remarks>
/// <para>
/// Release đầu cố định 10% (1000 bps). <c>PLATFORM_FEE_BPS</c> chỉ để override khi cần
/// test; production mặc định giữ 1000. KHÔNG cho phép sửa qua admin endpoint trong release này.
/// </para>
/// <para>
/// <see cref="PolicyVersion"/> lưu vào <c>Appointment.PolicyVersion</c> snapshot để khi scale
/// tỷ giá mới áp dụng, các booking cũ giữ nguyên phí đã ghi nhận.
/// </para>
/// </remarks>
public class PlatformPolicyOptions
{
    /// <summary>Section name trong appsettings.json và prefix env var (PLATFORM_POLICY_*).</summary>
    public const string SectionName = "PlatformPolicy";

    /// <summary>
    /// Platform fee tính theo basis points (bps). 1000 bps = 10.00%.
    /// Giá trị hợp lệ: 0..10000 (validate qua <see cref="Fitmate.Application.Common.Money.ValidateBps"/>).
    /// </summary>
    public int FeeBps { get; set; } = 1000;

    /// <summary>
    /// Version hiện hành của policy. Lưu vào snapshot booking để audit theo thời gian.
    /// </summary>
    public string PolicyVersion { get; set; } = "v1";

    /// <summary>
    /// Thời điểm policy có hiệu lực (UTC). Trả về qua API cho client biết.
    /// </summary>
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
}
