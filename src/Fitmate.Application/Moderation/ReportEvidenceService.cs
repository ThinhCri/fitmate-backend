using Fitmate.Application.Auth;
using Fitmate.Application.Storage;
using Fitmate.Domain.Common;
namespace Fitmate.Application.Moderation;
public sealed record ReportEvidenceRow(string TargetType, Guid TargetId, Guid ReporterId, string? ReporterName,
 Guid? AuthorId, string? AuthorName, string? Content, IReadOnlyList<string> ImageKeys, bool Available, DateTime? ContentUpdatedAt);
public sealed record ReportEvidenceView(string TargetType, Guid TargetId, Guid ReporterId, string? ReporterName,
 Guid? AuthorId, string? AuthorName, string? Content, IReadOnlyList<string> ImageUrls, bool Available, DateTime? ContentUpdatedAt);
public interface IReportEvidenceStore { Task<ReportEvidenceRow?> ReadAsync(Guid reportId, CancellationToken ct); }
public sealed class ReportEvidenceService(IReportEvidenceStore store, IAuthorizationGuard authz, ISignedUrlService urls)
{
 public async Task<ReportEvidenceView> ReadAsync(Guid adminId, Guid reportId, CancellationToken ct)
 {
  await authz.EnsureRoleAsync(adminId, "Admin", ct);
  var row = await store.ReadAsync(reportId, ct) ?? throw new NotFoundException(ErrorCodes.Moderation.ReportNotFound, "Report not found.");
  var images = row.ImageKeys.Take(10).Select(key => urls.CreatePresignedReadUrl(key, TimeSpan.FromMinutes(5)).ToString()).ToArray();
  return new(row.TargetType,row.TargetId,row.ReporterId,row.ReporterName,row.AuthorId,row.AuthorName,row.Content,images,row.Available,row.ContentUpdatedAt);
 }
}
