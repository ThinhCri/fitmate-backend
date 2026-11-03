using Fitmate.Application.Auth;
using Fitmate.Application.Moderation;
using Fitmate.Application.Storage;
using Fitmate.Domain.Common;
using Xunit;
namespace Fitmate.UnitTests;
public class ReportEvidenceServiceTests
{
 sealed class Store : IReportEvidenceStore {
  public bool Read; public ReportEvidenceRow? Row;
  public Task<ReportEvidenceRow?> ReadAsync(Guid id,CancellationToken ct) { Read=true; return Task.FromResult(Row); }
 }
 sealed class Guard : IAuthorizationGuard {
  public bool Deny;
  public Task EnsureRoleAsync(Guid id,string role,CancellationToken ct) { Assert.Equal("Admin",role); if(Deny) throw new ForbiddenException(ErrorCodes.Identity.Forbidden,"Denied"); return Task.CompletedTask; }
  public Task EnsureAnyRoleAsync(Guid id,IReadOnlyCollection<string> roles,CancellationToken ct)=>throw new NotSupportedException();
  public Task EnsureOwnerAsync(string r,Guid u,Guid o,CancellationToken ct)=>throw new NotSupportedException();
  public Task<string?> GetPrimaryRoleAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
  public Task<bool> HasRoleAsync(Guid id,string role,CancellationToken ct)=>throw new NotSupportedException();
 }
 sealed class Signer : ISignedUrlService {
  public List<string> Keys=new();
  public Uri CreatePresignedReadUrl(string key,TimeSpan ttl) { Assert.Equal(TimeSpan.FromMinutes(5),ttl); Keys.Add(key); return new Uri("https://media.example/"+key); }
  public string? TryValidateAndExtractPath(string path)=>null;
 }
 [Fact] public async Task DeniesBeforeReadingEvidence() {
  var store=new Store(); var service=new ReportEvidenceService(store,new Guard{Deny=true},new Signer());
  await Assert.ThrowsAsync<ForbiddenException>(()=>service.ReadAsync(Guid.NewGuid(),Guid.NewGuid(),default)); Assert.False(store.Read);
 }
 [Fact] public async Task MissingReportReturnsNotFound() {
  await Assert.ThrowsAsync<NotFoundException>(()=>new ReportEvidenceService(new Store(),new Guard(),new Signer()).ReadAsync(Guid.NewGuid(),Guid.NewGuid(),default));
 }
 [Fact] public async Task ReturnsContentAndBoundedExpiringImages() {
  var store=new Store{Row=new("Post",Guid.NewGuid(),Guid.NewGuid(),"Reporter",Guid.NewGuid(),"Author","Actual content",Enumerable.Range(0,12).Select(i=>"posts/"+i+".jpg").ToArray(),true,DateTime.UtcNow)};
  var signer=new Signer();var result=await new ReportEvidenceService(store,new Guard(),signer).ReadAsync(Guid.NewGuid(),Guid.NewGuid(),default);
  Assert.Equal("Actual content",result.Content); Assert.Equal("Author",result.AuthorName); Assert.Equal(10,result.ImageUrls.Count); Assert.DoesNotContain("storage://",string.Join(',',result.ImageUrls));
 }
 [Fact] public async Task UnavailableTargetDoesNotInventContent() {
  var store=new Store{Row=new("Post",Guid.NewGuid(),Guid.NewGuid(),null,null,null,null,Array.Empty<string>(),false,null)};
  var result=await new ReportEvidenceService(store,new Guard(),new Signer()).ReadAsync(Guid.NewGuid(),Guid.NewGuid(),default);
  Assert.False(result.Available);Assert.Null(result.Content);Assert.Empty(result.ImageUrls);
 }
}
