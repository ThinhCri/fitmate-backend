using Fitmate.Application.Auth;
using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

/// <summary>
/// BE-210: Moderation — admin list/resolve/dismiss reports (OpenAPI /admin/reports).
/// Resolve actions:
///   - Warn: only mark Resolved + admin note (target not affected).
///   - LockTarget: mark Resolved + admin note (caller is responsible for locking target; stub for now).
///   - DeleteContent: mark Resolved + admin note + soft-delete target Post/Comment if applicable.
///   - NoAction: same as Warn but reserved for "no violation found".
/// Dismiss: đóng report với lý do không hợp lệ; admin note optional.
/// Authorization 2 lớp: AuthorizationGuard.EnsureRoleAsync(Admin) + controller [Authorize(Policy=AdminOnly)].
/// </summary>
public class ModerationService : IModerationService
{
    private readonly IModerationStore _store;
    private readonly IAuthorizationGuard _authz;

    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxNoteLength = 1000;

    public ModerationService(IModerationStore store, IAuthorizationGuard authz)
    {
        _store = store;
        _authz = authz;
    }

    public async Task<ReportPage> ListReportsAsync(Guid adminUserId, ReportStatus? status, int page, int pageSize, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);

        if (page < 1) page = DefaultPage;
        if (pageSize < 1) pageSize = DefaultPageSize;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var skip = (page - 1) * pageSize;
        var (rows, total) = await _store.ListReportsAsync(status, skip, pageSize, ct);

        var items = rows.Select(ToView).ToList();
        return new ReportPage(items, total);
    }

    public async Task<ReportView> ResolveReportAsync(Guid adminUserId, Guid reportId, ReportResolveAction action, string? note, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        ValidateNote(note);

        var row = await _store.GetReportAsync(reportId, ct)
            ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, $"Report '{reportId}' not found.");
        if (row.Status != ReportStatus.Pending)
            throw new ConflictException(ErrorCodes.Moderation.ReportNotPending, $"Report '{reportId}' already {row.Status}.");

        var updated = row with
        {
            Status = ReportStatus.Resolved,
            AdminNote = ComposeNote(action.ToString(), note),
            ResolvedByAdminId = adminUserId,
            UpdatedAt = DateTime.UtcNow
        };
        await _store.SaveReportAsync(updated, ct);
        return ToView(updated);
    }

    public async Task<ReportView> DismissReportAsync(Guid adminUserId, Guid reportId, string? note, CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        ValidateNote(note);

        var row = await _store.GetReportAsync(reportId, ct)
            ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, $"Report '{reportId}' not found.");
        if (row.Status != ReportStatus.Pending)
            throw new ConflictException(ErrorCodes.Moderation.ReportNotPending, $"Report '{reportId}' already {row.Status}.");

        var updated = row with
        {
            Status = ReportStatus.Dismissed,
            AdminNote = note,
            ResolvedByAdminId = adminUserId,
            UpdatedAt = DateTime.UtcNow
        };
        await _store.SaveReportAsync(updated, ct);
        return ToView(updated);
    }

    private static void ValidateNote(string? note)
    {
        if (note is null) return;
        if (note.Length > MaxNoteLength)
            throw new ValidationException(ErrorCodes.Validation.TooLong, $"note > {MaxNoteLength} chars.",
                new Dictionary<string, string[]> { ["note"] = new[] { $"max {MaxNoteLength}" } });
    }

    private static string? ComposeNote(string action, string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return action;
        return $"{action}: {note.Trim()}";
    }

    private static ReportView ToView(ReportRow r) => new(
        Id: r.Id, ReporterId: r.ReporterId, TargetType: r.TargetType, TargetId: r.TargetId,
        Reason: r.Reason, Details: r.Details, Status: r.Status,
        AdminNote: r.AdminNote, ResolvedByAdminId: r.ResolvedByAdminId,
        CreatedAt: r.CreatedAt, UpdatedAt: r.UpdatedAt);
}
