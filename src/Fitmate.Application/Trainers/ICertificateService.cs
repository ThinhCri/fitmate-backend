using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Use case Certificate: PT submit, admin approve/reject, list cho cả 2 phía.
/// Theo domain-rules.md §7 — admin approve/reject phải audit.
/// </summary>
public interface ICertificateService
{
    Task<Certificate> GetForAdminAsync(Guid id, Guid adminId, CancellationToken ct);
    /// <summary>
    /// Admin list certificate theo PT, phân trang server-side.
    /// </summary>
    Task<CertificatePage> ListTrainerForAdminAsync(Guid ptUserId, Guid adminId, int page, int pageSize, CancellationToken ct);
    Task<Certificate> RevokeAsync(Guid id, Guid adminId, string reason, CancellationToken ct);
    /// <summary>
    /// PT submit certificate mới. Status = Pending. Nếu đã có cert Pending cho cùng (title, issuedBy, code)
    /// thì trả về cert đó (idempotent submit). <paramref name="imageUploadedByUserId"/> lưu chủ sở hữu
    /// thực sự của <paramref name="imageStorageKey"/>; null khi không upload ảnh hoặc khi không xác định được.
    /// </summary>
    Task<Certificate> SubmitAsync(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey, Guid? imageUploadedByUserId, CancellationToken ct);

    /// <summary>
    /// PT list certificate của mình (cả Pending/Approved/Rejected), phân trang (cap 100/1 page).
    /// </summary>
    Task<CertificatePage> ListMineAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Admin list certificate Pending (phân trang). Default pageSize = 50.
    /// </summary>
    Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingAsync(Guid adminUserId, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Admin approve cert. Throw ConflictException nếu không ở Pending. Audit log qua <see cref="IAuditLogger"/>.
    /// </summary>
    Task<Certificate> ApproveAsync(Guid certId, Guid adminUserId, CancellationToken ct);

    /// <summary>
    /// Admin reject cert. Lý do bắt buộc ≥ 5 ký tự. Throw ConflictException nếu không ở Pending,
    /// ValidationException nếu reason không hợp lệ.
    /// </summary>
    Task<Certificate> RejectAsync(Guid certId, Guid adminUserId, string reason, CancellationToken ct);
    Task<Certificate> RejectFraudAsync(Guid certId, Guid adminUserId, string reason, CancellationToken ct) =>
        throw new NotSupportedException();
}

/// <summary>
/// Audit log cho admin moderation. Application định nghĩa; Infrastructure sẽ có EF Core impl
/// ghi vào <c>audit_logs</c> (BE-110 sẽ mở rộng schema).
/// </summary>
public interface IAuditLogger
{
    /// <summary>Lưu audit log và SaveChanges trong transaction riêng.</summary>
    Task LogAsync(string action, string targetType, Guid targetId, Guid actorId, string? reason = null, CancellationToken ct = default);

    /// <summary>
    /// Stage một audit log entry vào DbContext mà KHÔNG SaveChanges; caller sẽ SaveChanges
    /// trong cùng transaction với business state change để đảm bảo atomicity (audit cannot miss).
    /// </summary>
    void Stage(string action, string targetType, Guid targetId, Guid actorId, string? reason = null);
}
