using Fitmate.Application.Auth;
using Fitmate.Application.Identity;
using Fitmate.Application.Moderation;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;

namespace Fitmate.Application.Moderation;

/// <summary>
/// BE-210 + BE-211: Moderation — admin list/resolve/dismiss reports.
/// Mở rộng BE-211: Warn (outbox user.warning), Lock (4 duration + stamp bump), DeleteContent (soft-delete Post/Comment),
/// idempotency (spam check), audit log.
/// Authorization 2 lớp: AuthorizationGuard.EnsureRoleAsync(Admin) + controller [Authorize(Policy=AdminOnly)].
/// </summary>
public class ModerationService : IModerationService
{
    private readonly IModerationStore _store;
    private readonly IModerationActionStore _actionStore;
    private readonly IAdminUserStore _adminStore;
    private readonly ISocialStore _socialStore;
    private readonly ICertificateStore _certStore;
    private readonly IUserRoleLookup _userLookup;
    private readonly IOutboxDispatcher _outbox;
    private readonly IAuditLogger _audit;
    private readonly IAuthorizationGuard _authz;

    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxNoteLength = 1000;

    public ModerationService(
        IModerationStore store,
        IModerationActionStore actionStore,
        IAdminUserStore adminStore,
        ISocialStore socialStore,
        ICertificateStore certStore,
        IUserRoleLookup userLookup,
        IOutboxDispatcher outbox,
        IAuditLogger audit,
        IAuthorizationGuard authz)
    {
        _store = store;
        _actionStore = actionStore;
        _adminStore = adminStore;
        _socialStore = socialStore;
        _certStore = certStore;
        _userLookup = userLookup;
        _outbox = outbox;
        _audit = audit;
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

    public async Task<ReportView> ResolveReportAsync(
        Guid adminUserId,
        Guid reportId,
        ReportResolveAction action,
        LockDuration? lockDuration,
        string? note,
        CancellationToken ct)
    {
        await _authz.EnsureRoleAsync(adminUserId, "Admin", ct);
        ValidateNote(note);

        var row = await _store.GetReportAsync(reportId, ct)
            ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, $"Report '{reportId}' not found.");
        if (row.Status != ReportStatus.Pending)
            throw new ConflictException(ErrorCodes.Moderation.ReportNotPending, $"Report '{reportId}' already {row.Status}.");

        // Map action enum → ResolveActionValue string.
        string resolveActionStr = action switch
        {
            ReportResolveAction.Warn => ResolveActionValue.Warn,
            ReportResolveAction.LockTarget => ResolveActionValue.LockPermanent, // override by duration below
            ReportResolveAction.DeleteContent => ResolveActionValue.DeleteContent,
            ReportResolveAction.NoAction => ResolveActionValue.NoAction,
            _ => throw new ValidationException(ErrorCodes.Moderation.InvalidResolveAction,
                $"Invalid action.", new Dictionary<string, string[]>())
        };

        // Determine target userId for warn/lock actions.
        var targetUserId = await ResolveTargetUserIdAsync(row.TargetType, row.TargetId, ct);

        // === Warn ===
        if (action == ReportResolveAction.Warn)
        {
            await RecordModerationActionAsync(targetUserId, adminUserId, reportId, UserModerationActionKind.Warn, null, note, ct);
            _outbox.Enqueue("user.warning", new UserWarningPayload(targetUserId, reportId, note ?? "", adminUserId));
            await _audit.LogAsync("report.resolve.warn", "Report", reportId, adminUserId,
                $"Warn: {note}", ct);
        }
        // === Lock ===
        else if (action == ReportResolveAction.LockTarget)
        {
            // Map LockDuration → ResolveActionValue string + DateTimeOffset.
            var (lockActionValue, expiresAtOffset) = lockDuration switch
            {
                LockDuration.Permanent => (ResolveActionValue.LockPermanent, (DateTimeOffset?)DateTimeOffset.MaxValue),
                LockDuration.Days30 => (ResolveActionValue.Lock30d, DateTimeOffset.UtcNow.AddDays(30)),
                LockDuration.Days7 => (ResolveActionValue.Lock7d, DateTimeOffset.UtcNow.AddDays(7)),
                LockDuration.Hours24 => (ResolveActionValue.Lock24h, DateTimeOffset.UtcNow.AddHours(24)),
                _ => throw new ValidationException(ErrorCodes.Moderation.InvalidResolveAction,
                    "lock_duration required when action=lock_target.",
                    new Dictionary<string, string[]> { ["lock_duration"] = new[] { "required" } })
            };
            resolveActionStr = lockActionValue;

            await _adminStore.SetLockAsync(targetUserId, expiresAtOffset, ct);
            await _adminStore.UpdateSecurityStampAsync(targetUserId, ct);
            await _adminStore.SaveChangesAsync(ct);
            // Lưu UTC DateTime cho UserModerationAction.ExpiresAt (theo convention domain).
            var expiresAtUtc = expiresAtOffset?.UtcDateTime;
            await RecordModerationActionAsync(targetUserId, adminUserId, reportId, UserModerationActionKind.Lock, expiresAtUtc, note, ct);
            await _audit.LogAsync("report.resolve.lock", "Report", reportId, adminUserId,
                $"{lockActionValue}: {note}", ct);
        }
        // === DeleteContent ===
        else if (action == ReportResolveAction.DeleteContent)
        {
            if (row.TargetType == ReportTargetType.Post)
            {
                await _store.SoftDeletePostAsync(row.TargetId, ct);
            }
            else if (row.TargetType == ReportTargetType.Comment)
            {
                await _store.SoftDeleteCommentAsync(row.TargetId, ct);
            }
            else
            {
                throw new ValidationException(ErrorCodes.Moderation.DeleteContentNotApplicable,
                    $"DeleteContent is only applicable for Post or Comment targets, not {row.TargetType}.",
                    new Dictionary<string, string[]>());
            }
            await _audit.LogAsync("report.resolve.delete_content", "Report", reportId, adminUserId,
                $"DeleteContent: {note}", ct);
        }
        // === NoAction ===
        else
        {
            await _audit.LogAsync("report.resolve.no_action", "Report", reportId, adminUserId,
                $"NoAction: {note}", ct);
        }

        // Update report status.
        var updated = row with
        {
            Status = ReportStatus.Resolved,
            ResolveAction = resolveActionStr,
            AdminNote = ComposeNote(resolveActionStr, note),
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
        await _audit.LogAsync("report.dismiss", "Report", reportId, adminUserId, note, ct);
        return ToView(updated);
    }

    private async Task<Guid> ResolveTargetUserIdAsync(ReportTargetType targetType, Guid targetId, CancellationToken ct)
    {
        return targetType switch
        {
            ReportTargetType.Post => await ResolvePostAuthorAsync(targetId, ct),
            ReportTargetType.Comment => await ResolveCommentAuthorAsync(targetId, ct),
            ReportTargetType.User => targetId,
            ReportTargetType.Certificate => await ResolveCertPtUserIdAsync(targetId, ct),
            _ => throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound,
                $"Cannot resolve target for type {targetType}.")
        };
    }

    private async Task<Guid> ResolvePostAuthorAsync(Guid postId, CancellationToken ct)
    {
        var post = await _socialStore.GetPostByIdAsync(postId, ct);
        return post?.AuthorId
            ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, $"Post '{postId}' not found.");
    }

    private async Task<Guid> ResolveCommentAuthorAsync(Guid commentId, CancellationToken ct)
    {
        var comment = await _socialStore.GetCommentAsync(commentId, ct);
        return comment?.AuthorId
            ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, $"Comment '{commentId}' not found.");
    }

    private async Task<Guid> ResolveCertPtUserIdAsync(Guid certId, CancellationToken ct)
    {
        var cert = await _certStore.FindByIdAsync(certId, ct);
        return cert?.PtUserId
            ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, $"Certificate '{certId}' not found.");
    }

    private async Task RecordModerationActionAsync(
        Guid userId,
        Guid adminId,
        Guid? reportId,
        UserModerationActionKind kind,
        DateTime? expiresAt,
        string? note,
        CancellationToken ct)
    {
        var action = UserModerationAction.Record(userId, kind, adminId, reportId, expiresAt, note);
        await _actionStore.AddAsync(action, ct);
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
        ResolveAction: r.ResolveAction,
        CreatedAt: r.CreatedAt, UpdatedAt: r.UpdatedAt);
}

internal record UserWarningPayload(Guid UserId, Guid ReportId, string Reason, Guid AdminId);
