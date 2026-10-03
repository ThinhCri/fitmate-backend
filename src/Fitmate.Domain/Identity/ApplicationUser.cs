using System;
using Microsoft.AspNetCore.Identity;

namespace Fitmate.Domain.Identity;

/// <summary>
/// User aggregate chính của Fitmate. Theo DEC-032, kế thừa <see cref="IdentityUser{TKey}"/>
/// để tận dụng ASP.NET Core Identity: lockout, email confirm, password reset,
/// 2FA hook, token providers. Giữ schema chuẩn <c>asp_net_users</c>.
///
/// Chính sách chính:
/// - Tất cả user identity do Identity quản lý (IdentityUser{TKey}).
/// - <see cref="Domain.Common.UserRole"/> chuyển sang Identity role
///   (cột <c>role</c> bị bỏ; thay bằng <c>asp_net_user_roles</c>).
/// - Field custom: <see cref="FullName"/>, <see cref="Status"/>, <see cref="Timezone"/>,
///   <see cref="LastActiveAt"/>, <see cref="DeletedAt"/>.
/// - Avatar URL là tham chiếu media do Fitmate quản lý.
/// - Soft delete dùng <see cref="DeletedAt"/> + global filter.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public string? AvatarStorageKey { get; set; }

    /// <summary>
    /// Trạng thái tài khoản mở rộng (Active/Locked/PendingDeletion).
    /// Độc lập với Identity Lockout; Identity Lockout dùng cho failed-password attempts,
    /// còn <see cref="Domain.Common.UserStatus"/> dùng cho admin moderation.
    /// </summary>
    public Domain.Common.UserStatus Status { get; set; } = Domain.Common.UserStatus.Active;

    /// <summary>
    /// IANA timezone, mặc định Asia/Ho_Chi_Minh (xem <c>domain-rules.md §4</c>).
    /// </summary>
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    public DateTime? LastActiveAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token (IdentityUser đã có ConcurrencyStamp, nhưng giữ alias).
    /// </summary>
    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsDeleted => DeletedAt.HasValue;
}
