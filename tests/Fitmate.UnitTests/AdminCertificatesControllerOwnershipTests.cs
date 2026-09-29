using System.Security.Claims;
using Fitmate.Api.Auth;
using Fitmate.Api.Trainers;
using Fitmate.Application.Auth;
using Fitmate.Application.Storage;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Trainers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-CERT-02: ownership check for certificate preview URL.
/// Contract: <c>/admin/certificates/{id}/preview</c> must reject when
/// <list type="bullet">
///   <item>no <c>ImageUploadedByUserId</c> is recorded (legacy data), or</item>
///   <item>the recorded uploader is different from the certificate owner.</item>
/// </list>
/// Prefix-only ownership (the prior implementation) was insufficient;
/// see docs/review-handoff/changes.md (F3 follow-up).
/// </summary>
public class AdminCertificatesControllerOwnershipTests
{
    private sealed class StubSignedUrlService : ISignedUrlService
    {
        public Uri? LastUrl { get; private set; }
        public string? LastKey { get; private set; }
        public Uri CreatePresignedReadUrl(string relativePath, TimeSpan ttl)
        {
            LastKey = relativePath;
            LastUrl = new Uri($"http://localhost/uploads/{relativePath}?sig=test");
            return LastUrl;
        }
        // Controller Preview không dùng validation path; trả null là hợp lệ.
        public string? TryValidateAndExtractPath(string pathAndQuery) => null;
    }

    private sealed class CapturingAudit : IAuditLogger
    {
        public List<string> Audits { get; } = new();
        public Task LogAsync(string action, string targetType, Guid targetId, Guid actorId, string? reason = null, CancellationToken ct = default)
        {
            Audits.Add(action);
            return Task.CompletedTask;
        }
        public void Stage(string action, string targetType, Guid targetId, Guid actorId, string? reason = null)
            => Audits.Add($"stage:{action}");
    }

    private sealed class FakeCurrentUser : ICurrentUserAccessor
    {
        public Guid? UserId { get; set; }
        public Guid? GetUserId() => UserId;
        public Guid GetUserIdOrThrow() => UserId ?? throw new InvalidOperationException("missing admin id");
    }

    private sealed class InMemoryCertService : ICertificateService
    {
        public List<Certificate> Certs { get; } = new();
        public Certificate? FindById(Guid id) => Certs.FirstOrDefault(c => c.Id == id);

        // Delegated impls reuse CertificateService for non-preview paths; tests below
        // override only the methods exercised in controller.Preview.
        public Task<Certificate> GetForAdminAsync(Guid id, Guid adminId, CancellationToken ct)
            => Task.FromResult(FindById(id) ?? throw new InvalidOperationException("missing"));

        public Task<Certificate> ApproveAsync(Guid certId, Guid adminUserId, CancellationToken ct) => throw new NotImplementedException();
        public Task<Certificate> RejectAsync(Guid certId, Guid adminUserId, string reason, CancellationToken ct) => throw new NotImplementedException();
        public Task<Certificate> RevokeAsync(Guid id, Guid adminId, string reason, CancellationToken ct) => throw new NotImplementedException();
        public Task<Certificate> SubmitAsync(Guid ptUserId, string title, string issuedBy, string code, DateOnly issueDate, string? imageStorageKey, Guid? imageUploadedByUserId, CancellationToken ct) => throw new NotImplementedException();
        public Task<CertificatePage> ListMineAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingAsync(Guid adminUserId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
        public Task<CertificatePage> ListTrainerForAdminAsync(Guid ptUserId, Guid adminId, int page, int pageSize, CancellationToken ct) => throw new NotImplementedException();
    }

    private static AdminCertificatesController BuildController(Certificate cert, out FakeCurrentUser current, out StubSignedUrlService urls, out CapturingAudit audit)
    {
        var store = new InMemoryCertService();
        store.Certs.Add(cert);
        current = new FakeCurrentUser { UserId = Guid.NewGuid() };
        urls = new StubSignedUrlService();
        audit = new CapturingAudit();
        var controller = new AdminCertificatesController(store)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    [Fact]
    public async Task Preview_WhenNoUploaderRecorded_Returns_NotFound()
    {
        var pt = Guid.NewGuid();
        var cert = Certificate.Submit(pt, "T", "I", "C", new DateOnly(2024, 1, 1), "certificates/x.png", imageUploadedByUserId: null);
        var controller = BuildController(cert, out var current, out var urls, out var audit);

        var result = await controller.Preview(cert.Id, current, urls, audit, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        urls.LastUrl.Should().BeNull();
        audit.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_WhenImageKeyMissing_Returns_NotFound()
    {
        var pt = Guid.NewGuid();
        // Cert được upload bởi PT nhưng storage key rỗng.
        var cert = Certificate.Submit(pt, "T", "I", "C", new DateOnly(2024, 1, 1), imageStorageKey: null, imageUploadedByUserId: pt);
        var controller = BuildController(cert, out var current, out var urls, out var audit);

        var result = await controller.Preview(cert.Id, current, urls, audit, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
        urls.LastUrl.Should().BeNull();
        audit.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_WhenUploaderDiffersFromOwner_Returns_403()
    {
        var pt = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        // Cer t được upload bởi user khác (không phải PT sở hữu cert).
        var cert = Certificate.Submit(pt, "T", "I", "C", new DateOnly(2024, 1, 1), "certificates/x.png", imageUploadedByUserId: otherUser);
        var controller = BuildController(cert, out var current, out var urls, out var audit);

        var result = await controller.Preview(cert.Id, current, urls, audit, CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
        urls.LastUrl.Should().BeNull();
        audit.Audits.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_WhenUploaderMatchesOwner_ReturnsSignedUrl_AndAudits()
    {
        var pt = Guid.NewGuid();
        var cert = Certificate.Submit(pt, "T", "I", "C", new DateOnly(2024, 1, 1), "certificates/x.png", imageUploadedByUserId: pt);
        var controller = BuildController(cert, out var current, out var urls, out var audit);

        var result = await controller.Preview(cert.Id, current, urls, audit, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
        urls.LastKey.Should().Be("certificates/x.png");
        audit.Audits.Should().ContainSingle().Which.Should().Be("certificate.preview");
    }
}
