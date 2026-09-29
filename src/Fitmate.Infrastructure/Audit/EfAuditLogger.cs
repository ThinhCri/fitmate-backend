using Fitmate.Application.Trainers;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;

namespace Fitmate.Infrastructure.Audit;

/// <summary>
/// EF Core-backed <see cref="IAuditLogger"/> ghi vào bảng <c>audit_logs</c>.
/// </summary>
public class EfAuditLogger : IAuditLogger
{
    private readonly FitmateDbContext _db;

    public EfAuditLogger(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string action, string targetType, Guid targetId, Guid actorId, string? reason = null, CancellationToken ct = default)
    {
        var entry = Build(action, targetType, targetId, actorId, reason);
        await _db.AuditLogs.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
    }

    public void Stage(string action, string targetType, Guid targetId, Guid actorId, string? reason = null)
    {
        // Caller owns the SaveChangesAsync so it can run inside the same transaction
        // as the business state change. See CertificateService.Revoke/Approve/Reject.
        _db.AuditLogs.Add(Build(action, targetType, targetId, actorId, reason));
    }

    private static AuditLog Build(string action, string targetType, Guid targetId, Guid actorId, string? reason) =>
        AuditLog.Record(
            actorId: actorId,
            actorRole: "Admin",
            action: action,
            targetType: targetType,
            targetId: targetId,
            reason: reason);
}
