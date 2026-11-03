using Fitmate.Domain.Common;
using Fitmate.Application.Moderation;
using Fitmate.Domain.Moderation;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Fitmate.Infrastructure.Moderation;
public sealed class EfReportEvidenceStore(FitmateDbContext db) : IReportEvidenceStore
{
 public async Task<ReportEvidenceRow?> ReadAsync(Guid reportId, CancellationToken ct)
 {
  var report = await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
  if(report is null) return null;
  Guid? authorId = null; string? content = null; DateTime? updated = null; bool available = false;
  var keys = new List<string>();
  if(report.TargetType == ReportTargetType.Post) {
   var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == report.TargetId, ct);
   if(post is not null) { authorId=post.AuthorId; content=post.Content; updated=post.UpdatedAt; available=true;
    keys.AddRange(post.ImageStorageKeys); if(keys.Count==0 && post.ImageStorageKey is not null) keys.Add(post.ImageStorageKey); }
  } else if(report.TargetType == ReportTargetType.Comment) {
   var comment = await db.Comments.AsNoTracking().FirstOrDefaultAsync(c => c.Id == report.TargetId, ct);
   if(comment is not null) { authorId=comment.AuthorId; content=comment.Text; updated=comment.UpdatedAt; available=true; }
  } else if(report.TargetType == ReportTargetType.User) {
   var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == report.TargetId, ct);
   if(user is not null) { authorId=user.Id; content=user.FullName; available=true; }
  } else if(report.TargetType == ReportTargetType.Certificate) {
   var cert = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == report.TargetId, ct);
   if(cert is not null) { authorId=cert.PtUserId; content=cert.Title + " — " + cert.IssuedBy + " — " + cert.Code; updated=cert.UpdatedAt; available=true;
    if(cert.ImageUploadedByUserId == cert.PtUserId && cert.ImageStorageKey is not null) keys.Add(cert.ImageStorageKey); }
  }
  var reporterName = await db.Users.AsNoTracking().Where(u => u.Id == report.ReporterId).Select(u => u.FullName).FirstOrDefaultAsync(ct);
  var authorName = authorId.HasValue ? await db.Users.AsNoTracking().Where(u => u.Id == authorId.Value).Select(u => u.FullName).FirstOrDefaultAsync(ct) : null;
  return new(report.TargetType.ToString(),report.TargetId,report.ReporterId,reporterName,authorId,authorName,content,keys,available,updated);
 }
}
