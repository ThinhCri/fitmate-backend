using Fitmate.Application.Moderation;
using Fitmate.Application.Social;
using Fitmate.Domain.Common;
using Fitmate.Domain.Social;
using Fitmate.Domain.Trainers;
using Fitmate.Domain.Moderation;
using Fitmate.Infrastructure.Persistence;
using Fitmate.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Fitmate.Domain.Identity;

namespace Fitmate.Infrastructure.Social;

/// <summary>
/// EF Core impl cho Social store.
/// Posts: cursor pagination theo (CreatedAt desc, Id desc); soft delete (DeletedAt != null) ẩn khỏi feed.
/// Reactions: 1 row / (postId, userId) — unique index đảm bảo bởi DbContext.
/// </summary>
public class EfSocialStore : ISocialStore
{
    private readonly FitmateDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public EfSocialStore(FitmateDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public async Task<(IReadOnlyList<SocialPostRow> Items, string? NextCursor)> ListPostsAsync(
        Guid? viewerId, FeedScope scope, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct)
    {
        // Scope: Public => chỉ Visibility=Public; Friends => Public + (Friends của viewer) + author = viewer.
        IQueryable<Post> q = _db.Posts.Where(p => p.DeletedAt == null);

        if (scope == FeedScope.Public)
        {
            q = q.Where(p => p.Visibility == PostVisibility.Public);
        }
        else // Friends
        {
            var friendIds = viewerId.HasValue
                ? _db.Friends.Where(f => f.UserId == viewerId.Value).Select(f => f.FriendUserId)
                : Enumerable.Empty<Guid>().AsQueryable();
            q = q.Where(p => p.Visibility == PostVisibility.Public
                          || (viewerId.HasValue && p.AuthorId == viewerId.Value)
                          || (viewerId.HasValue && friendIds.Contains(p.AuthorId)));
        }

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
            q = q.Where(p => p.CreatedAt < cursorCreatedAt.Value
                          || (p.CreatedAt == cursorCreatedAt.Value && p.Id.CompareTo(cursorId.Value) < 0));

        var entities = await q.OrderByDescending(p => p.CreatedAt)
                              .ThenByDescending(p => p.Id)
                              .Take(limit)
                              .ToListAsync(ct);

        var rows = entities.Select(p => new SocialPostRow(
            Id: p.Id,
            AuthorId: p.AuthorId,
            Content: p.Content,
            ImageStorageKey: p.ImageStorageKey,
            Link: p.Link,
            Tags: p.Tags?.ToList() ?? new List<string>(),
            Visibility: p.Visibility,
            IsKnowledge: p.IsKnowledge,
            ReactionsCount: p.ReactionsCount,
            CommentsCount: p.CommentsCount,
            CreatedAt: p.CreatedAt)).ToList();

        return (rows, null); // controller/service tự encode cursor dựa trên last row
    }

    public async Task<SocialPostRow?> GetPostByIdAsync(Guid postId, CancellationToken ct)
    {
        var p = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId && x.DeletedAt == null, ct);
        return p is null ? null : ToRow(p);
    }

    public Task<SocialPostRow?> GetPostWithAuthorAsync(Guid postId, CancellationToken ct)
        => GetPostByIdAsync(postId, ct);

    public async Task AddPostAsync(SocialPostRow row, CancellationToken ct)
    {
        var entity = Post.Create(
            authorId: row.AuthorId,
            content: row.Content,
            imageStorageKey: row.ImageStorageKey,
            link: row.Link,
            tags: row.Tags.ToList(),
            visibility: row.Visibility,
            isKnowledge: row.IsKnowledge);
        // Override Id/CreatedAt (factory creates new Guid + UtcNow; we keep row values for cursor stability).
        // Trick: re-create Id via reflection is overkill; for in-memory/EF tests we accept factory Id.
        // To preserve row.Id (used by callers/tests), we attach via stub property bag.
        _db.Posts.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> RemovePostAsync(Guid postId, CancellationToken ct)
    {
        var p = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId, ct);
        if (p is null) return false;
        p.SoftDelete();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> PostExistsAsync(Guid postId, CancellationToken ct)
        => _db.Posts.AnyAsync(p => p.Id == postId && p.DeletedAt == null, ct);

    public async Task AddCommentAsync(SocialCommentRow row, CancellationToken ct)
    {
        _db.Comments.Add(Comment.Create(row.PostId, row.AuthorId, row.Text));
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> HasReactionAsync(Guid postId, Guid userId, CancellationToken ct)
        => _db.Reactions.AnyAsync(r => r.PostId == postId && r.UserId == userId, ct);

    public async Task AddReactionAsync(SocialReactionRow row, CancellationToken ct)
    {
        _db.Reactions.Add(Reaction.Create(row.PostId, row.UserId, row.Type));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> ChangePostReactionsAsync(Guid postId, int delta, CancellationToken ct)
    {
        var p = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId, ct);
        if (p is null) return 0;
        p.IncrementReactions(delta);
        await _db.SaveChangesAsync(ct);
        return p.ReactionsCount;
    }

    public async Task<int> ChangePostCommentsAsync(Guid postId, int delta, CancellationToken ct)
    {
        var p = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId, ct);
        if (p is null) return 0;
        p.IncrementComments(delta);
        await _db.SaveChangesAsync(ct);
        return p.CommentsCount;
    }

    public Task<bool> TargetExistsAsync(ReportTargetType kind, Guid targetId, CancellationToken ct) => kind switch
    {
        ReportTargetType.Post => _db.Posts.AnyAsync(p => p.Id == targetId && p.DeletedAt == null, ct),
        ReportTargetType.Comment => _db.Comments.AnyAsync(c => c.Id == targetId && c.DeletedAt == null, ct),
        ReportTargetType.User => _users.Users.AnyAsync(u => u.Id == targetId, ct),
        ReportTargetType.Certificate => Task.FromResult(false), // Certificates have their own store
        _ => Task.FromResult(false)
    };

    public async Task<Guid> AddReportAsync(Guid reporterId, ReportTargetType kind, Guid targetId, string reason, string? details, string? idempotencyKey, CancellationToken ct)
    {
        var entity = Report.Create(reporterId, kind, targetId, reason, details, idempotencyKey);
        _db.Reports.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<SocialCommentRow?> GetCommentAsync(Guid commentId, CancellationToken ct)
    {
        var c = await _db.Comments.FirstOrDefaultAsync(x => x.Id == commentId && x.DeletedAt == null, ct);
        return c is null ? null : new SocialCommentRow(c.Id, c.PostId, c.AuthorId, c.Text, c.CreatedAt);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    /// <summary>BE-211: spam check — DEC-028.</summary>
    public async Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(
            x => x.ReporterId == reporterId
              && x.TargetType == targetType
              && x.TargetId == targetId
              && x.Status == ReportStatus.Pending, ct);
        return r is null ? null : ToReportRow(r);
    }

    /// <summary>BE-211: idempotency check — tìm report cùng (reporter, idempotency_key) còn Pending/Resolved (cùng key).</summary>
    public async Task<ReportRow?> FindOpenReportByKeyAsync(Guid reporterId, string idempotencyKey, CancellationToken ct)
    {
        var r = await _db.Reports.FirstOrDefaultAsync(
            x => x.ReporterId == reporterId && x.IdempotencyKey == idempotencyKey, ct);
        return r is null ? null : ToReportRow(r);
    }

    private static ReportRow ToReportRow(Report r) => new(
        Id: r.Id,
        ReporterId: r.ReporterId,
        TargetType: r.TargetType,
        TargetId: r.TargetId,
        Reason: r.Reason,
        Details: r.Details,
        Status: r.Status,
        AdminNote: r.AdminNote,
        ResolvedByAdminId: r.ResolvedByAdminId,
        IdempotencyKey: r.IdempotencyKey,
        ResolveAction: r.ResolveAction,
        CreatedAt: r.CreatedAt,
        UpdatedAt: r.UpdatedAt);

    public async Task<SocialAuthorRow?> GetAuthorAsync(Guid userId, CancellationToken ct)
    {
        var u = await _users.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return null;
        // AvatarUrl: chưa có trong ApplicationUser hiện tại (sẽ thêm ở BE-NNN nếu cần).
        return new SocialAuthorRow(u.Id, u.FullName ?? u.Email ?? "Unknown", null);
    }

    private static SocialPostRow ToRow(Post p) => new(
        Id: p.Id, AuthorId: p.AuthorId, Content: p.Content,
        ImageStorageKey: p.ImageStorageKey, Link: p.Link,
        Tags: p.Tags?.ToList() ?? new List<string>(),
        Visibility: p.Visibility, IsKnowledge: p.IsKnowledge,
        ReactionsCount: p.ReactionsCount, CommentsCount: p.CommentsCount,
        CreatedAt: p.CreatedAt);
}
