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

        public Task<IReadOnlyList<Certificate>> ListByPtAsync(Guid ptUserId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Certificate>>(Items.Values
                .Where(c => c.PtUserId == ptUserId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList());

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
        var first = await svc.SubmitAsync(pt, "ACE", "ACE", "ACE-001", new DateOnly(2024, 1, 1), null, CancellationToken.None);
        var second = await svc.SubmitAsync(pt, "ACE", "ACE", "ACE-001", new DateOnly(2024, 1, 1), null, CancellationToken.None);

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
        var act = () => svc.SubmitAsync(Guid.NewGuid(), title, issuedBy, code, new DateOnly(2024, 1, 1), null, CancellationToken.None);
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

        var list = await NewService(store, new CapturingAudit()).ListMineAsync(pt, CancellationToken.None);
        list.Should().HaveCount(3);
        list[0].Title.Should().Be("T3"); // mới nhất
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

        var (items, total) = await svc.ListPendingAsync(page: 1, pageSize: 10000, ct: CancellationToken.None);
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
}
