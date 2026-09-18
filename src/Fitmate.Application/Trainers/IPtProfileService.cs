using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Use case quản lý PtProfile. Theo domain-rules.md §1, PT tự cập nhật profile;
/// <c>is_verified</c> chỉ admin set thông qua certificate approval.
/// </summary>
public interface IPtProfileService
{
    /// <summary>
    /// Tạo draft profile cho user role = pt. Idempotent: nếu đã có profile → trả về profile hiện tại.
    /// </summary>
    Task<PtProfile> EnsureDraftAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Cập nhật specialty / experience / price / bio. Không cho sửa nếu user không phải PT.
    /// </summary>
    Task<PtProfile> UpdateAsync(Guid userId, string? specialty, int? experienceYears, long? pricePerSessionVnd, string? bio, CancellationToken ct);

    /// <summary>
    /// Lấy profile theo userId. Trả null nếu chưa có.
    /// </summary>
    Task<PtProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);
}
