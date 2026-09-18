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
        var entry = AuditLog.Record(
            actorId: actorId,
            actorRole: "Admin",
            action: action,
            targetType: targetType,
            targetId: targetId,
            reason: reason);
        await _db.AuditLogs.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
    }
}
