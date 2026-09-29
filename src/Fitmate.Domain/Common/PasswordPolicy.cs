namespace Fitmate.Domain.Common;

/// <summary>
/// Password policy cho toàn hệ thống Fitmate.
///
/// Chốt 2026-09-28 (SET-W01): mọi đường tạo/đổi/reset mật khẩu dùng chung một giới hạn
/// dưới đây. Trước đó các nơi lệch nhau: FluentAuth validators dùng 8, ASP.NET Identity và
/// CLI seed dùng 12, Web Admin form dùng 12, OpenAPI ghi 8. Người dùng có thể đăng ký được
/// mật khẩu 8 ký tự qua API rồi không đổi được nữa vì form Admin yêu cầu 12 — một vòng bất khả thi.
///
/// Phạm vi: chỉ áp dụng cho mật khẩu MỚI (register, change, reset, seed admin).
/// Không có tác động tới hash hiện có và không buộc tài khoản đang tồn tại phải đổi mật khẩu.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Số ký tự tối thiểu (UTF-16 code units, khớp <c>IdentityOptions.Password.RequiredLength</c>).</summary>
    public const int MinimumLength = 12;

    /// <summary>Số ký tự tối đa. Giữ nguyên như trước để không thu hẹp hành vi ngoài phạm vi.</summary>
    public const int MaximumLength = 128;

    /// <summary>
    /// Thông điệp dùng chung cho cả validation message và UI copy, để client không phải
    /// tự dịch số 12 (tránh lệch khi policy đổi).
    /// </summary>
    public const string MinimumLengthMessage = "Password must be at least 12 characters";

    /// <summary>Kiểm tra nhanh dùng cho CLI/seed và test. Không thay thế FluentValidation.</summary>
    public static bool IsAcceptable(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length >= MinimumLength
        && password.Length <= MaximumLength;
}
