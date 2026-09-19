using Fitmate.Application.Auth;
using Fitmate.Application.Moderation;
using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-210: ModerationService — list/resolve/dismiss reports + AuthorizationGuard.
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

    private sealed class InMemoryStore : IModerationStore
    {
        public Dictionary<Guid, ReportRow> Reports { get; } = new();

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

        public Task SaveReportAsync(ReportRow row, CancellationToken ct)
        {
            Reports[row.Id] = row;
            return Task.CompletedTask;
        }
    }

    private static ModerationService NewService(InMemoryStore store, string? requiredRole = "Admin")
        => new(store, new RoleRequireAuthz { RequiredRole = requiredRole });

    private static ReportRow NewReport(ReportStatus status = ReportStatus.Pending)
        => new(Guid.NewGuid(), Guid.NewGuid(), ReportTargetType.Post, Guid.NewGuid(),
               "spam content here", "details", status, null, null,
               DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(-1));

    [Fact]
    public async Task List_AllPending_ByDefault()
    {
        var store = new InMemoryStore();
        store.Reports[Guid.NewGuid()] = NewReport(ReportStatus.Pending);
        store.Reports[Guid.NewGuid()] = NewReport(ReportStatus.Resolved);
        store.Reports[Guid.NewGuid()] = NewReport(ReportStatus.Dismissed);

        var page = await NewService(store).ListReportsAsync(Guid.NewGuid(), null, 1, 20, CancellationToken.None);
        page.Total.Should().Be(3);
        page.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task List_StatusFilter_PendingOnly()
    {
        var store = new InMemoryStore();
        store.Reports[Guid.NewGuid()] = NewReport(ReportStatus.Pending);
        store.Reports[Guid.NewGuid()] = NewReport(ReportStatus.Pending);
        store.Reports[Guid.NewGuid()] = NewReport(ReportStatus.Resolved);

        var page = await NewService(store).ListReportsAsync(Guid.NewGuid(), ReportStatus.Pending, 1, 20, CancellationToken.None);
        page.Total.Should().Be(2);
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_PageSizeClamped()
    {
        var store = new InMemoryStore();
        for (var i = 0; i < 200; i++) store.Reports[Guid.NewGuid()] = NewReport();
        var page = await NewService(store).ListReportsAsync(Guid.NewGuid(), null, 1, 1000, CancellationToken.None);
        page.Items.Should().HaveCount(100); // MaxPageSize
    }

    [Fact]
    public async Task List_PageZero_DefaultsToOne()
    {
        var store = new InMemoryStore();
        store.Reports[Guid.NewGuid()] = NewReport();
        var page = await NewService(store).ListReportsAsync(Guid.NewGuid(), null, 0, 20, CancellationToken.None);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task List_NonAdmin_Forbidden()
    {
        var svc = NewService(new InMemoryStore(), requiredRole: "Pt");
        var act = () => svc.ListReportsAsync(Guid.NewGuid(), null, 1, 20, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Resolve_PendingReport_MarksResolved()
    {
        var store = new InMemoryStore();
        var r = NewReport();
        store.Reports[r.Id] = r;
        var adminId = Guid.NewGuid();

        var result = await NewService(store).ResolveReportAsync(adminId, r.Id, ReportResolveAction.Warn, "spam confirmed", CancellationToken.None);
        result.Status.Should().Be(ReportStatus.Resolved);
        result.AdminNote.Should().Be("Warn: spam confirmed");
        result.ResolvedByAdminId.Should().Be(adminId);
        store.Reports[r.Id].Status.Should().Be(ReportStatus.Resolved);
    }

    [Fact]
    public async Task Resolve_AlreadyResolved_Conflict()
    {
        var store = new InMemoryStore();
        var r = NewReport(ReportStatus.Resolved);
        store.Reports[r.Id] = r;
        var act = () => NewService(store).ResolveReportAsync(Guid.NewGuid(), r.Id, ReportResolveAction.Warn, "x", CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Moderation.ReportNotPending);
    }

    [Fact]
    public async Task Resolve_NotFound_NotFound()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.ResolveReportAsync(Guid.NewGuid(), Guid.NewGuid(), ReportResolveAction.Warn, null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Moderation.ReportNotFound);
    }

    [Fact]
    public async Task Resolve_AllActions_Accepted()
    {
        foreach (var action in new[] { ReportResolveAction.Warn, ReportResolveAction.LockTarget, ReportResolveAction.DeleteContent, ReportResolveAction.NoAction })
        {
            var store = new InMemoryStore();
            var r = NewReport();
            store.Reports[r.Id] = r;
            var result = await NewService(store).ResolveReportAsync(Guid.NewGuid(), r.Id, action, "note", CancellationToken.None);
            result.Status.Should().Be(ReportStatus.Resolved);
            result.AdminNote.Should().StartWith(action.ToString());
        }
    }

    [Fact]
    public async Task Resolve_NoteTooLong_Rejected()
    {
        var store = new InMemoryStore();
        var r = NewReport();
        store.Reports[r.Id] = r;
        var act = () => NewService(store).ResolveReportAsync(Guid.NewGuid(), r.Id, ReportResolveAction.Warn, new string('x', 1001), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooLong);
    }

    [Fact]
    public async Task Resolve_EmptyNote_OnlyActionName()
    {
        var store = new InMemoryStore();
        var r = NewReport();
        store.Reports[r.Id] = r;
        var result = await NewService(store).ResolveReportAsync(Guid.NewGuid(), r.Id, ReportResolveAction.NoAction, null, CancellationToken.None);
        result.AdminNote.Should().Be("NoAction");
    }

    [Fact]
    public async Task Dismiss_PendingReport_MarksDismissed()
    {
        var store = new InMemoryStore();
        var r = NewReport();
        store.Reports[r.Id] = r;
        var adminId = Guid.NewGuid();

        var result = await NewService(store).DismissReportAsync(adminId, r.Id, "not a violation", CancellationToken.None);
        result.Status.Should().Be(ReportStatus.Dismissed);
        result.AdminNote.Should().Be("not a violation");
        result.ResolvedByAdminId.Should().Be(adminId);
    }

    [Fact]
    public async Task Dismiss_AlreadyDismissed_Conflict()
    {
        var store = new InMemoryStore();
        var r = NewReport(ReportStatus.Dismissed);
        store.Reports[r.Id] = r;
        var act = () => NewService(store).DismissReportAsync(Guid.NewGuid(), r.Id, null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Dismiss_NotFound_NotFound()
    {
        var svc = NewService(new InMemoryStore());
        var act = () => svc.DismissReportAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Dismiss_NoteTooLong_Rejected()
    {
        var store = new InMemoryStore();
        var r = NewReport();
        store.Reports[r.Id] = r;
        var act = () => NewService(store).DismissReportAsync(Guid.NewGuid(), r.Id, new string('x', 1001), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooLong);
    }
}
