using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-040: CertificateService — submit / list / approve / reject với in-memory store + audit log.
/// </summary>
public class CertificateServiceTests
{
    private sealed class InMemoryStore : ICertificateStore
    {
        public Dictionary<Guid, Certificate> Items { get; } = new();
        public int SaveCalls { get; private set; }

        public Task<Certificate?> FindByIdentityAsync(Guid ptUserId, string title, string issuedBy, string code, CancellationToken ct)
        {
            var hit = Items.Values.FirstOrDefault(c =>
                c.PtUserId == ptUserId &&
                c.Title == title &&
                c.IssuedBy == issuedBy &&
                c.Code == code &&
                c.Status == CertificateStatus.Pending);
            return Task.FromResult(hit);
        }

        public Task<Certificate?> FindByIdAsync(Guid certId, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(certId));

        public async Task<(IReadOnlyList<Certificate> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 25;
            if (pageSize > 100) pageSize = 100;
            var all = Items.Values
                .Where(c => c.PtUserId == ptUserId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();
            var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            await Task.CompletedTask;
            return (items, all.Count);
        }

        public Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingPagedAsync(int page, int pageSize, CancellationToken ct)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 100) pageSize = 100;

            var pending = Items.Values.Where(c => c.Status == CertificateStatus.Pending).OrderBy(c => c.CreatedAt).ToList();
            var items = pending.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<Certificate>, int)>((items, pending.Count));
        }

        public Task AddAsync(Certificate cert, CancellationToken ct)
        {
            Items[cert.Id] = cert;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            SaveCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingAudit : IAuditLogger
    {
        public List<(string Action, string TargetType, Guid TargetId, Guid ActorId, string? Reason)> Calls { get; } = new();
        public Task LogAsync(string action, string targetType, Guid targetId, Guid actorId, string? reason = null, CancellationToken ct = default)
        {
            Calls.Add((action, targetType, targetId, actorId, reason));
            return Task.CompletedTask;
        }
        public void Stage(string action, string targetType, Guid targetId, Guid actorId, string? reason = null)
            => Calls.Add((action, targetType, targetId, actorId, reason));
    }

    private static CertificateService NewService(InMemoryStore store, CapturingAudit audit)
        => new(store, audit, AlwaysAllowAuthz.Instance);

    [Fact]
    public async Task Submit_NewCert_PersistsAsPending()
    {
        var store = new InMemoryStore();
        var audit = new CapturingAudit();
        var svc = NewService(store, audit);

        var cert = await svc.SubmitAsync(
            ptUserId: Guid.NewGuid(),
            title: "NASM CPT",
            issuedBy: "NASM",
            code: "CPT-12345",
            issueDate: new DateOnly(2024, 6, 1),
            imageStorageKey: null,
            imageUploadedByUserId: null,
            ct: CancellationToken.None);

        cert.Status.Should().Be(CertificateStatus.Pending);
        cert.Title.Should().Be("NASM CPT");
        store.Items.Should().HaveCount(1);
        store.SaveCalls.Should().Be(1);
        audit.Calls.Should().BeEmpty(); // submit không audit
    }

    [Fact]
    public async Task Submit_DuplicateIdentity_ReturnsExisting()
    {
        var store = new InMemoryStore();
        var audit = new CapturingAudit();
        var svc = NewService(store, audit);

        var pt = Guid.NewGuid();
        var first = await svc.SubmitAsync(pt, "ACE", "ACE", "ACE-001", new DateOnly(2024, 1, 1), null, null, CancellationToken.None);
        var second = await svc.SubmitAsync(pt, "ACE", "ACE", "ACE-001", new DateOnly(2024, 1, 1), null, null, CancellationToken.None);

        first.Id.Should().Be(second.Id);
        store.Items.Should().HaveCount(1);
        store.SaveCalls.Should().Be(1); // chỉ save lần đầu
    }

    [Theory]
    [InlineData("", "ACE", "ACE-001")]
    [InlineData("ACE", "", "ACE-001")]
    [InlineData("ACE", "ACE", "")]
    [InlineData("ACE-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "ACE", "ACE-001")]
    [InlineData("ACE", "ACE-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", "ACE-001")]
    [InlineData("ACE", "ACE", "ACE-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX")]
    public async Task Submit_InvalidFields_Throws(string title, string issuedBy, string code)
    {
        var svc = NewService(new InMemoryStore(), new CapturingAudit());
        var act = () => svc.SubmitAsync(Guid.NewGuid(), title, issuedBy, code, new DateOnly(2024, 1, 1), null, null, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ListMine_ReturnsCertsForPt_OrderedDesc()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        var c1 = Certificate.Submit(pt, "T1", "I1", "C1", new DateOnly(2024, 1, 1), null);
        var c2 = Certificate.Submit(pt, "T2", "I2", "C2", new DateOnly(2024, 2, 1), null);
        var c3 = Certificate.Submit(pt, "T3", "I3", "C3", new DateOnly(2024, 3, 1), null);
        store.Items[c1.Id] = c1; store.Items[c2.Id] = c2; store.Items[c3.Id] = c3;

        var page = await NewService(store, new CapturingAudit()).ListMineAsync(pt, page: 1, pageSize: 50, ct: CancellationToken.None);
        page.Items.Should().HaveCount(3);
        page.Items[0].Title.Should().Be("T3"); // mới nhất
        page.Total.Should().Be(3);
    }

    [Fact]
    public async Task ListPending_Pagination_ClampsPageSize()
    {
        var store = new InMemoryStore();
        // Tạo 5 cert Pending.
        for (int i = 0; i < 5; i++)
        {
            var c = Certificate.Submit(Guid.NewGuid(), $"T{i}", "I", $"C{i}", new DateOnly(2024, 1, 1), null);
            store.Items[c.Id] = c;
        }
        var svc = NewService(store, new CapturingAudit());

        var (items, total) = await svc.ListPendingAsync(Guid.NewGuid(), page: 1, pageSize: 10000, ct: CancellationToken.None);
        items.Count.Should().Be(5);
        total.Should().Be(5);
    }

    [Fact]
    public async Task Approve_PendingCert_MarksApprovedAndAudits()
    {
        var store = new InMemoryStore();
        var audit = new CapturingAudit();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", new DateOnly(2024, 1, 1), null);
        store.Items[cert.Id] = cert;

        var admin = Guid.NewGuid();
        var approved = await NewService(store, audit).ApproveAsync(cert.Id, admin, CancellationToken.None);

        approved.Status.Should().Be(CertificateStatus.Approved);
        approved.VerifiedByAdminId.Should().Be(admin);
        approved.VerifiedAt.Should().NotBeNull();
        audit.Calls.Should().ContainSingle(c => c.Action == "certificate.approve" && c.ActorId == admin);
    }

    [Fact]
    public async Task Approve_AlreadyApproved_ThrowsConflict()
    {
        var store = new InMemoryStore();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", new DateOnly(2024, 1, 1), null);
        cert.Approve(Guid.NewGuid());
        store.Items[cert.Id] = cert;

        var svc = NewService(store, new CapturingAudit());
        var act = () => svc.ApproveAsync(cert.Id, Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .Where(ex => ex.Code == ErrorCodes.Trainer.CertificateNotPending);
    }

    [Fact]
    public async Task Approve_NotFound_ThrowsNotFound()
    {
        var svc = NewService(new InMemoryStore(), new CapturingAudit());
        var act = () => svc.ApproveAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Reject_PendingCert_RequiresReasonMin5Chars()
    {
        var store = new InMemoryStore();
        var audit = new CapturingAudit();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", new DateOnly(2024, 1, 1), null);
        store.Items[cert.Id] = cert;
        var svc = NewService(store, audit);

        var act = () => svc.RejectAsync(cert.Id, Guid.NewGuid(), "no", CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Code == ErrorCodes.Trainer.CertificateMissingReason);
    }

    [Fact]
    public async Task Reject_ValidReason_MarksRejectedAndAudits()
    {
        var store = new InMemoryStore();
        var audit = new CapturingAudit();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", new DateOnly(2024, 1, 1), null);
        store.Items[cert.Id] = cert;
        var admin = Guid.NewGuid();

        var rejected = await NewService(store, audit).RejectAsync(cert.Id, admin, "Image mờ, không đọc được", CancellationToken.None);

        rejected.Status.Should().Be(CertificateStatus.Rejected);
        rejected.RejectReason.Should().Be("Image mờ, không đọc được");
        rejected.VerifiedByAdminId.Should().Be(admin);
        audit.Calls.Should().ContainSingle(c => c.Action == "certificate.reject" && c.Reason == "Image mờ, không đọc được");
    }

    [Fact]
    public async Task Reject_AlreadyRejected_ThrowsConflict()
    {
        var store = new InMemoryStore();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", new DateOnly(2024, 1, 1), null);
        cert.Reject(Guid.NewGuid(), "lý do ban đầu");
        store.Items[cert.Id] = cert;

        var svc = NewService(store, new CapturingAudit());
        var act = () => svc.RejectAsync(cert.Id, Guid.NewGuid(), "lý do lần 2", CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    // ─── CERT-01: server pagination khi admin list certificate theo PT ───────────

    [Fact]
    public async Task ListTrainer_Pagination_ReturnsCorrectSlice()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        // Tạo 7 cert, CreatedAt tăng dần → desc sẽ trả T6 → T0.
        var ordered = Enumerable.Range(0, 7)
            .Select(i => Certificate.Submit(pt, $"T{i}", "I{i}", $"C{i}",
                new DateOnly(2024, 1, 1).AddDays(i), null))
            .ToList();
        ordered.ForEach(c => store.Items[c.Id] = c);
        var svc = NewService(store, new CapturingAudit());

        // pageSize > 100 phải được clamp về 100.
        var page1 = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 1, pageSize: 250, ct: CancellationToken.None);
        page1.Total.Should().Be(7);
        page1.PageSize.Should().Be(100); // clamped
        page1.Items.Should().HaveCount(7); // chỉ có 7 cert

        // Page nhỏ với pageSize=3:
        var pageFirst = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 1, pageSize: 3, ct: CancellationToken.None);
        pageFirst.Items.Should().HaveCount(3);
        pageFirst.Page.Should().Be(1);
        pageFirst.PageSize.Should().Be(3);
        pageFirst.Total.Should().Be(7);

        var pageThird = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 3, pageSize: 3, ct: CancellationToken.None);
        pageThird.Items.Should().HaveCount(1); // 7 % 3 = 1 phần tử cuối

        // page < 1 → clamp 1.
        var clamped = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 0, pageSize: 5, ct: CancellationToken.None);
        clamped.Page.Should().Be(1);
    }

    [Fact]
    public async Task ListTrainer_PageSize_BelowOne_FallsBackToDefault()
    {
        // pageSize <= 0 không được tạo Skip/Take âm (lỗi runtime của EF) — fallback về default.
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        store.Items[Guid.NewGuid()] = Certificate.Submit(pt, "T0", "I0", "C0", new DateOnly(2024, 1, 1), null);
        var svc = NewService(store, new CapturingAudit());

        var zero = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 1, pageSize: 0, ct: CancellationToken.None);
        zero.PageSize.Should().Be(25);
        zero.Items.Should().HaveCount(1);

        var negative = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 1, pageSize: -10, ct: CancellationToken.None);
        negative.PageSize.Should().Be(25);
        negative.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListTrainer_NegativePage_ClampsToFirstPage()
    {
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
            store.Items[Guid.NewGuid()] = Certificate.Submit(pt, $"T{i}", $"I{i}", $"C{i}", new DateOnly(2024, 1, 1).AddDays(i), null);
        var svc = NewService(store, new CapturingAudit());

        var result = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: -5, pageSize: 2, ct: CancellationToken.None);
        result.Page.Should().Be(1);
        result.Items.Should().HaveCount(2);
        result.Total.Should().Be(3);
    }

    [Fact]
    public async Task ListTrainer_PageBeyondEnd_ReturnsEmptySliceButTotal()
    {
        // Trang vượt quá số bản ghi vẫn phải trả total đúng, không phải 0.
        var store = new InMemoryStore();
        var pt = Guid.NewGuid();
        store.Items[Guid.NewGuid()] = Certificate.Submit(pt, "T0", "I0", "C0", new DateOnly(2024, 1, 1), null);
        var svc = NewService(store, new CapturingAudit());

        var result = await svc.ListTrainerForAdminAsync(pt, Guid.NewGuid(), page: 99, pageSize: 25, ct: CancellationToken.None);
        result.Items.Should().BeEmpty();
        result.Total.Should().Be(1);
    }

    // ─── CERT-02: ownership check cho preview URL ───────────────────────────────
    //
    // Vẫn cover bằng CertificateService khi cert có/không ImageUploadedByUserId.

    [Fact]
    public void Certificate_Submit_PersistsImageUploadedByUserId()
    {
        var pt = Guid.NewGuid();
        var cert = Certificate.Submit(pt, "T", "I", "C", new DateOnly(2024, 1, 1), "certificates/abc.png", pt);
        cert.ImageStorageKey.Should().Be("certificates/abc.png");
        cert.ImageUploadedByUserId.Should().Be(pt);
    }

    [Fact]
    public void Certificate_Submit_WithoutUploader_LeavesUploadedByNull()
    {
        var pt = Guid.NewGuid();
        var cert = Certificate.Submit(pt, "T", "I", "C", new DateOnly(2024, 1, 1), "certificates/abc.png", imageUploadedByUserId: null);
        cert.ImageUploadedByUserId.Should().BeNull();
    }
}
