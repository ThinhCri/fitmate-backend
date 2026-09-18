using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Use case Certificate: PT submit, admin approve/reject, list cho cả 2 phía.
/// Theo domain-rules.md §7 — admin approve/reject phải audit.
/// </summary>
public interface ICertificateService
{
    /// <summary>
    /// PT submit certificate mới. Status = Pending. Nếu đã có cert Pending cho cùng (title, issuedBy, code)
    /// thì trả về cert đó (idempotent submit).
    /// </summary>
    Task<Certificate> SubmitAsync(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey, CancellationToken ct);

    /// <summary>
    /// PT list certificate của mình (cả Pending/Approved/Rejected).
    /// </summary>
    Task<IReadOnlyList<Certificate>> ListMineAsync(Guid ptUserId, CancellationToken ct);

    /// <summary>
    /// Admin list certificate Pending (phân trang). Default pageSize = 50.
    /// </summary>
    Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingAsync(int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Admin approve cert. Throw ConflictException nếu không ở Pending. Audit log qua <see cref="IAuditLogger"/>.
    /// </summary>
    Task<Certificate> ApproveAsync(Guid certId, Guid adminUserId, CancellationToken ct);

    /// <summary>
    /// Admin reject cert. Lý do bắt buộc ≥ 5 ký tự. Throw ConflictException nếu không ở Pending,
    /// ValidationException nếu reason không hợp lệ.
    /// </summary>
    Task<Certificate> RejectAsync(Guid certId, Guid adminUserId, string reason, CancellationToken ct);
}

/// <summary>
/// Audit log cho admin moderation. Application định nghĩa; Infrastructure sẽ có EF Core impl
/// ghi vào <c>audit_logs</c> (BE-110 sẽ mở rộng schema).
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(string action, string targetType, Guid targetId, Guid actorId, string? reason = null, CancellationToken ct = default);
}
