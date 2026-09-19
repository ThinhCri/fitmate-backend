using Fitmate.Application.Auth;
using Fitmate.Application.Identity;
using Fitmate.Application.Moderation;
using Fitmate.Application.Outbox;
using Fitmate.Application.Social;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Moderation;
using Fitmate.Domain.Social;
using Fitmate.Domain.Trainers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-210 + BE-211: ModerationService — list/resolve/dismiss reports + AuthorizationGuard.
/// </summary>
public class ModerationServiceTests
{
    private sealed class RoleRequireAuthz : IAuthorizationGuard
    {
        public string? RequiredRole { get; set; }
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct)
        {
            if (RequiredRole != null && !string.Equals(role, RequiredRole, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(ErrorCodes.Identity.Forbidden, $"Requires role {role}.");
            return Task.CompletedTask;
        }
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>(RequiredRole);
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(RequiredRole != null);
    }

    private sealed class InMemoryModerationStore : IModerationStore
    {
        // Key = ReportRow.Id để GetReportAsync hoạt động đúng.
        public Dictionary<Guid, ReportRow> Reports { get; } = new();
        public List<Guid> SoftDeletedPosts { get; } = new();
        public List<Guid> SoftDeletedComments { get; } = new();

        public Task<(IReadOnlyList<ReportRow> Items, long Total)> ListReportsAsync(
            ReportStatus? status, int skip, int take, CancellationToken ct)
        {
            IEnumerable<ReportRow> q = Reports.Values;
            if (status.HasValue) q = q.Where(r => r.Status == status.Value);
            var ordered = q.OrderByDescending(r => r.CreatedAt).ToList();
            var total = ordered.Count;
            var page = ordered.Skip(skip).Take(take).ToList();
            return Task.FromResult<(IReadOnlyList<ReportRow>, long)>((page, total));
        }

        public Task<ReportRow?> GetReportAsync(Guid reportId, CancellationToken ct)
            => Task.FromResult<ReportRow?>(Reports.GetValueOrDefault(reportId));

        public Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct)
            => Task.FromResult<ReportRow?>(Reports.Values.FirstOrDefault(r =>
                r.ReporterId == reporterId && r.TargetType == targetType && r.TargetId == targetId && r.Status == ReportStatus.Pending));

        public Task SaveReportAsync(ReportRow row, CancellationToken ct)
        {
            Reports[row.Id] = row;
            return Task.CompletedTask;
        }

        public Task SoftDeletePostAsync(Guid postId, CancellationToken ct) { SoftDeletedPosts.Add(postId); return Task.CompletedTask; }
        public Task SoftDeleteCommentAsync(Guid commentId, CancellationToken ct) { SoftDeletedComments.Add(commentId); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryModerationActionStore : IModerationActionStore
    {
        public List<UserModerationAction> Actions { get; } = new();
        public Task AddAsync(UserModerationAction action, CancellationToken ct) { Actions.Add(action); return Task.CompletedTask; }
    }

    private sealed class InMemoryAdminUserStore : IAdminUserStore
    {
        public Dictionary<Guid, (DateTimeOffset? lockEnd, DateTime? stamp)> Users { get; } = new();
        public Task<(IReadOnlyList<AdminUserSummary> Items, int Total)> ListPagedAsync(AdminUserListQuery query, CancellationToken ct) => throw new NotImplementedException();
        public Task<AdminUserSummary?> FindSummaryAsync(Guid userId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> SetLockAsync(Guid userId, DateTimeOffset? lockoutEndUtc, CancellationToken ct)
        {
            Users.TryGetValue(userId, out var u);
            Users[userId] = (lockoutEndUtc, u.stamp);
            return Task.FromResult(true);
        }
        public Task UpdateSecurityStampAsync(Guid userId, CancellationToken ct)
        {
            Users.TryGetValue(userId, out var u);
            Users[userId] = (u.lockEnd, DateTime.UtcNow);
            return Task.CompletedTask;
        }
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>("Admin");
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryOutboxDispatcher : IOutboxDispatcher
    {
        public List<(string Type, object Payload)> Enqueued { get; } = new();
        public void Enqueue(string type, object payload) => Enqueued.Add((type, payload));
    }

    private sealed class InMemoryAuditLogger : IAuditLogger
    {
        public List<(string Action, string TargetType, Guid TargetId, Guid ActorId, string? Reason)> Entries { get; } = new();
        public Task LogAsync(string action, string targetType, Guid targetId, Guid actorId, string? reason = null, CancellationToken ct = default)
        {
            Entries.Add((action, targetType, targetId, actorId, reason));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySocialStore : ISocialStore
    {
        public Dictionary<Guid, SocialPostRow> Posts { get; } = new();
        public Dictionary<Guid, SocialCommentRow> Comments { get; } = new();
        public Task<(IReadOnlyList<SocialPostRow> Items, string? NextCursor)> ListPostsAsync(Guid? viewerId, FeedScope scope, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct) => throw new NotImplementedException();
        public Task<SocialPostRow?> GetPostByIdAsync(Guid postId, CancellationToken ct) => Task.FromResult<SocialPostRow?>(Posts.GetValueOrDefault(postId));
        public Task<SocialPostRow?> GetPostWithAuthorAsync(Guid postId, CancellationToken ct) => GetPostByIdAsync(postId, ct);
        public Task AddPostAsync(SocialPostRow row, CancellationToken ct) { Posts[row.Id] = row; return Task.CompletedTask; }
        public Task<bool> RemovePostAsync(Guid postId, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> PostExistsAsync(Guid postId, CancellationToken ct) => Task.FromResult(Posts.ContainsKey(postId));
        public Task AddCommentAsync(SocialCommentRow row, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> HasReactionAsync(Guid postId, Guid userId, CancellationToken ct) => throw new NotImplementedException();
        public Task AddReactionAsync(SocialReactionRow row, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> ChangePostReactionsAsync(Guid postId, int delta, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> ChangePostCommentsAsync(Guid postId, int delta, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> TargetExistsAsync(ReportTargetType kind, Guid targetId, CancellationToken ct) => Task.FromResult(true);
        public Task<Guid> AddReportAsync(Guid reporterId, ReportTargetType kind, Guid targetId, string reason, string? details, string? idempotencyKey, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<SocialCommentRow?> GetCommentAsync(Guid commentId, CancellationToken ct) => Task.FromResult<SocialCommentRow?>(Comments.GetValueOrDefault(commentId));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct) => Task.FromResult<ReportRow?>(null);
        public Task<ReportRow?> FindOpenReportByKeyAsync(Guid reporterId, string idempotencyKey, CancellationToken ct) => Task.FromResult<ReportRow?>(null);
        public Task<SocialAuthorRow?> GetAuthorAsync(Guid userId, CancellationToken ct) => Task.FromResult<SocialAuthorRow?>(null);
    }

    private sealed class InMemoryCertificateStore : ICertificateStore
    {
        public Dictionary<Guid, Certificate> Certs { get; } = new();
        public Task<Certificate?> FindByIdentityAsync(Guid ptUserId, string title, string issuedBy, string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<Certificate?> FindByIdAsync(Guid certId, CancellationToken ct) => Task.FromResult<Certificate?>(Certs.GetValueOrDefault(certId));
        public Task<IReadOnlyList<Certificate>> ListByPtAsync(Guid ptUserId, CancellationToken ct) => throw new NotImplementedException();
        public Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingPagedAsync(int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task AddAsync(Certificate cert, CancellationToken ct) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemoryUserRoleLookup : IUserRoleLookup
    {
        public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct) => Task.FromResult(true);
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>("Member");
    }

    private static ModerationService NewService(
        InMemoryModerationStore modStore,
        InMemorySocialStore socialStore,
        InMemoryCertificateStore certStore,
        InMemoryAdminUserStore adminStore,
        InMemoryOutboxDispatcher outbox,
        InMemoryAuditLogger audit,
        string? requiredRole = "Admin")
    {
        return new ModerationService(
            modStore,
            new InMemoryModerationActionStore(),
            adminStore,
            socialStore,
            certStore,
            new InMemoryUserRoleLookup(),
            outbox,
            audit,
            new RoleRequireAuthz { RequiredRole = requiredRole });
    }

    private static (Guid postId, Guid ptUserId, Guid reporterId, Guid adminId, Guid postAuthorId) SetupPostScenario(InMemorySocialStore social, InMemoryModerationStore modStore)
    {
        var postAuthor = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        social.Posts[postId] = new SocialPostRow(postId, postAuthor, "spam content", null, null, new List<string>(), PostVisibility.Public, false, 0, 0, DateTime.UtcNow);
        var row = NewReport(ReportTargetType.Post, postId, reporterId);
        modStore.Reports[row.Id] = row;
        return (postId, postAuthor, reporterId, adminId, postAuthor);
    }

    private static ReportRow NewReport(ReportTargetType targetType = ReportTargetType.Post, Guid? targetId = null, Guid? reporterId = null, ReportStatus status = ReportStatus.Pending)
    {
        var id = Guid.NewGuid();
        return new ReportRow(id,
               reporterId ?? Guid.NewGuid(),
               targetType,
               targetId ?? Guid.NewGuid(),
               "spam content here",
               "details",
               status,
               null,
               null,
               null,
               null,
               DateTime.UtcNow.AddMinutes(-1),
               DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task List_AllPending_ByDefault()
    {
        var store = new InMemoryModerationStore();
        store.Reports[Guid.NewGuid()] = NewReport(status: ReportStatus.Pending);
        store.Reports[Guid.NewGuid()] = NewReport(status: ReportStatus.Resolved);
        store.Reports[Guid.NewGuid()] = NewReport(status: ReportStatus.Dismissed);

        var svc = NewService(store, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var page = await svc.ListReportsAsync(Guid.NewGuid(), null, 1, 20, CancellationToken.None);
        page.Total.Should().Be(3);
        page.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task List_StatusFilter_PendingOnly()
    {
        var store = new InMemoryModerationStore();
        store.Reports[Guid.NewGuid()] = NewReport(status: ReportStatus.Pending);
        store.Reports[Guid.NewGuid()] = NewReport(status: ReportStatus.Pending);
        store.Reports[Guid.NewGuid()] = NewReport(status: ReportStatus.Resolved);

        var svc = NewService(store, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var page = await svc.ListReportsAsync(Guid.NewGuid(), ReportStatus.Pending, 1, 20, CancellationToken.None);
        page.Total.Should().Be(2);
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_PageSizeClamped()
    {
        var store = new InMemoryModerationStore();
        for (var i = 0; i < 200; i++) store.Reports[Guid.NewGuid()] = NewReport();
        var svc = NewService(store, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var page = await svc.ListReportsAsync(Guid.NewGuid(), null, 1, 1000, CancellationToken.None);
        page.Items.Should().HaveCount(100); // MaxPageSize
    }

    [Fact]
    public async Task List_PageZero_DefaultsToOne()
    {
        var store = new InMemoryModerationStore();
        store.Reports[Guid.NewGuid()] = NewReport();
        var svc = NewService(store, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var page = await svc.ListReportsAsync(Guid.NewGuid(), null, 0, 20, CancellationToken.None);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task List_NonAdmin_Forbidden()
    {
        var store = new InMemoryModerationStore();
        var svc = NewService(store, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger(), requiredRole: "Pt");
        var act = () => svc.ListReportsAsync(Guid.NewGuid(), null, 1, 20, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Resolve_Warn_EnqueuesOutboxAndRecordsAction()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();
        var (postId, postAuthorId, _, _, _) = SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();
        adminStore.Users[postAuthorId] = (null, (DateTime?)null);

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
        var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.Warn, null, "spam confirmed", CancellationToken.None);

        result.Status.Should().Be(ReportStatus.Resolved);
        result.ResolveAction.Should().Be("warn");
        outbox.Enqueued.Should().ContainSingle(e => e.Type == "user.warning");
        audit.Entries.Should().Contain(e => e.Action == "report.resolve.warn" && e.TargetId == reportRow.Id);
    }

    [Fact]
    public async Task Resolve_LockPermanent_SetsLockAndRecordsAction()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();
        var (postId, postAuthorId, _, _, _) = SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();
        adminStore.Users[postAuthorId] = (null, (DateTime?)null);

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
        var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.LockTarget, LockDuration.Permanent, "spam abuse", CancellationToken.None);

        result.Status.Should().Be(ReportStatus.Resolved);
        result.ResolveAction.Should().Be("lock_permanent");
        adminStore.Users[postAuthorId].lockEnd.Should().Be(DateTimeOffset.MaxValue);
        audit.Entries.Should().Contain(e => e.Action == "report.resolve.lock");
    }

    [Fact]
    public async Task Resolve_Lock30d_SetsLockWithExpiresAt()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();
        var (postId, postAuthorId, _, _, _) = SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();
        adminStore.Users[postAuthorId] = (null, (DateTime?)null);

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
        var before = DateTimeOffset.UtcNow;
        var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.LockTarget, LockDuration.Days30, "30-day suspension", CancellationToken.None);

        result.ResolveAction.Should().Be("lock_30d");
        var lockEnd = adminStore.Users[postAuthorId].lockEnd!.Value;
        lockEnd.Should().BeCloseTo(before.AddDays(30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Resolve_Lock7d_And_24h()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();

        foreach (var duration in new[] { LockDuration.Days7, LockDuration.Hours24 })
        {
            modStore.Reports.Clear();
            var (postId, postAuthorId, _, _, _) = SetupPostScenario(socialStore, modStore);
            var reportRow = modStore.Reports.Values.First();
            adminStore.Users[postAuthorId] = (null, (DateTime?)null);

            var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
            var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.LockTarget, duration, null, CancellationToken.None);
            var expected = duration switch
            {
                LockDuration.Days7 => "lock_7d",
                LockDuration.Hours24 => "lock_24h",
                _ => ""
            };
            result.ResolveAction.Should().Be(expected);
        }
    }

    [Fact]
    public async Task Resolve_Lock_WithoutDuration_Rejected()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var (postId, postAuthorId, _, _, _) = SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.LockTarget, null, "missing duration", CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Moderation.InvalidResolveAction);
    }

    [Fact]
    public async Task Resolve_DeleteContent_Post_SoftDeletes()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();
        var (postId, _, _, _, _) = SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
        var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.DeleteContent, null, "violates TOS", CancellationToken.None);

        result.Status.Should().Be(ReportStatus.Resolved);
        result.ResolveAction.Should().Be("delete_content");
        modStore.SoftDeletedPosts.Should().Contain(postId);
        audit.Entries.Should().Contain(e => e.Action == "report.resolve.delete_content");
    }

    [Fact]
    public async Task Resolve_DeleteContent_Comment_SoftDeletes()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();

        var commentId = Guid.NewGuid();
        var commentAuthor = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        socialStore.Comments[commentId] = new SocialCommentRow(commentId, Guid.NewGuid(), commentAuthor, "offensive", DateTime.UtcNow);
        modStore.Reports[reportId] = NewReport(targetType: ReportTargetType.Comment, targetId: commentId, reporterId: reporterId);

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
        var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportId, ReportResolveAction.DeleteContent, null, null, CancellationToken.None);

        result.Status.Should().Be(ReportStatus.Resolved);
        modStore.SoftDeletedComments.Should().Contain(commentId);
    }

    [Fact]
    public async Task Resolve_DeleteContent_UserTarget_Rejected()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var reportId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        modStore.Reports[reportId] = NewReport(targetType: ReportTargetType.User, targetId: targetUserId);

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.ResolveReportAsync(Guid.NewGuid(), reportId, ReportResolveAction.DeleteContent, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Moderation.DeleteContentNotApplicable);
    }

    [Fact]
    public async Task Resolve_NoAction_MarksResolved()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var adminStore = new InMemoryAdminUserStore();
        var outbox = new InMemoryOutboxDispatcher();
        var audit = new InMemoryAuditLogger();
        SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(), adminStore, outbox, audit);
        var result = await svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.NoAction, null, "no violation", CancellationToken.None);

        result.Status.Should().Be(ReportStatus.Resolved);
        result.ResolveAction.Should().Be("no_action");
        audit.Entries.Should().Contain(e => e.Action == "report.resolve.no_action");
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_Conflict()
    {
        var modStore = new InMemoryModerationStore();
        var reportId = Guid.NewGuid();
        modStore.Reports[reportId] = NewReport(status: ReportStatus.Resolved);

        var svc = NewService(modStore, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.ResolveReportAsync(Guid.NewGuid(), reportId, ReportResolveAction.Warn, null, "x", CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Moderation.ReportNotPending);
    }

    [Fact]
    public async Task Resolve_NotFound_NotFound()
    {
        var svc = NewService(new InMemoryModerationStore(), new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.ResolveReportAsync(Guid.NewGuid(), Guid.NewGuid(), ReportResolveAction.Warn, null, null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Moderation.ReportNotFound);
    }

    [Fact]
    public async Task Resolve_NoteTooLong_Rejected()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var (postId, _, _, _, _) = SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.ResolveReportAsync(Guid.NewGuid(), reportRow.Id, ReportResolveAction.Warn, null, new string('x', 1001), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooLong);
    }

    [Fact]
    public async Task Dismiss_PendingReport_MarksDismissed()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        var audit = new InMemoryAuditLogger();
        SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();

        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), audit);
        var result = await svc.DismissReportAsync(Guid.NewGuid(), reportRow.Id, "not a violation", CancellationToken.None);

        result.Status.Should().Be(ReportStatus.Dismissed);
        result.AdminNote.Should().Be("not a violation");
        audit.Entries.Should().Contain(e => e.Action == "report.dismiss");
    }

    [Fact]
    public async Task Dismiss_AlreadyDismissed_Conflict()
    {
        var modStore = new InMemoryModerationStore();
        var reportId = Guid.NewGuid();
        modStore.Reports[reportId] = NewReport(status: ReportStatus.Dismissed);

        var svc = NewService(modStore, new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.DismissReportAsync(Guid.NewGuid(), reportId, null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Dismiss_NotFound_NotFound()
    {
        var svc = NewService(new InMemoryModerationStore(), new InMemorySocialStore(), new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.DismissReportAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Dismiss_NoteTooLong_Rejected()
    {
        var modStore = new InMemoryModerationStore();
        var socialStore = new InMemorySocialStore();
        SetupPostScenario(socialStore, modStore);
        var reportRow = modStore.Reports.Values.First();
        var svc = NewService(modStore, socialStore, new InMemoryCertificateStore(),
            new InMemoryAdminUserStore(), new InMemoryOutboxDispatcher(), new InMemoryAuditLogger());
        var act = () => svc.DismissReportAsync(Guid.NewGuid(), reportRow.Id, new string('x', 1001), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooLong);
    }
}
