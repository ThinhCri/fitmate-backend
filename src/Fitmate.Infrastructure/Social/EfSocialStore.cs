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
            // BE-181: query canonical friendship — user có thể nằm ở low hoặc high.
            IQueryable<Guid> friendIds;
            if (viewerId.HasValue)
            {
                var v = viewerId.Value;
                friendIds = _db.Friendships
                    .Where(f => f.UserLowId == v || f.UserHighId == v)
                    .Select(f => f.UserLowId == v ? f.UserHighId : f.UserLowId);
            }
            else
            {
                friendIds = Enumerable.Empty<Guid>().AsQueryable();
            }
            q = q.Where(p => p.Visibility == PostVisibility.Public
                          || (viewerId.HasValue && p.AuthorId == viewerId.Value)
                          || (viewerId.HasValue && friendIds.Contains(p.AuthorId)));
        }

        if (viewerId.HasValue)
        {
            var v = viewerId.Value;
            var blockedUserIds = _db.BlockedUsers
                .Where(b => b.BlockerId == v || b.BlockedId == v)
                .Select(b => b.BlockerId == v ? b.BlockedId : b.BlockerId);
            q = q.Where(p => !blockedUserIds.Contains(p.AuthorId));
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
            ImageStorageKeys: p.ImageStorageKeys?.ToList() ?? (p.ImageStorageKey is null ? new() : new() { p.ImageStorageKey }),
            Link: p.Link,
            Tags: p.Tags?.ToList() ?? new List<string>(),
            Visibility: p.Visibility,
            IsKnowledge: p.IsKnowledge,
            ReactionsCount: p.ReactionsCount,
            CommentsCount: p.CommentsCount,
            CreatedAt: p.CreatedAt)).ToList();

        return (rows, null); // controller/service tự encode cursor dựa trên last row
    }

    public async Task<IReadOnlyList<SocialPostRow>> ListBookmarkedPostsAsync(
        Guid viewerId, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct)
    {
        var friendIds = _db.Friendships
            .Where(f => f.UserLowId == viewerId || f.UserHighId == viewerId)
            .Select(f => f.UserLowId == viewerId ? f.UserHighId : f.UserLowId);
        var blockedUserIds = _db.BlockedUsers
            .Where(b => b.BlockerId == viewerId || b.BlockedId == viewerId)
            .Select(b => b.BlockerId == viewerId ? b.BlockedId : b.BlockerId);

        var query = from bookmark in _db.PostBookmarks
                    join post in _db.Posts on bookmark.PostId equals post.Id
                    where bookmark.UserId == viewerId
                       && post.DeletedAt == null
                       && !blockedUserIds.Contains(post.AuthorId)
                       && (post.Visibility == PostVisibility.Public
                           || post.AuthorId == viewerId
                           || (post.Visibility == PostVisibility.Friends && friendIds.Contains(post.AuthorId)))
                    select post;

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
            query = query.Where(post => post.CreatedAt < cursorCreatedAt.Value
                || (post.CreatedAt == cursorCreatedAt.Value && post.Id.CompareTo(cursorId.Value) < 0));

        var entities = await query.OrderByDescending(post => post.CreatedAt)
            .ThenByDescending(post => post.Id)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(ToRow).ToList();
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
            isKnowledge: row.IsKnowledge,
            imageStorageKeys: row.ImageStorageKeys,
            id: row.Id,
            createdAt: row.CreatedAt);
        _db.Posts.Add(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SocialPostRow?> UpdatePostVisibilityAsync(Guid postId, PostVisibility visibility, CancellationToken ct)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId && x.DeletedAt == null, ct);
        if (post is null) return null;
        post.ChangeVisibility(visibility);
        await _db.SaveChangesAsync(ct);
        return ToRow(post);
    }

    public async Task<SocialPostRow?> UpdatePostAsync(Guid postId, string content, IReadOnlyList<string> imageStorageKeys, IReadOnlyList<string> tags, bool isKnowledge, CancellationToken ct)
    {
        var post = await _db.Posts.FirstOrDefaultAsync(x => x.Id == postId && x.DeletedAt == null, ct);
        if (post is null) return null;
        post.Update(content, imageStorageKeys, tags, isKnowledge);
        await _db.SaveChangesAsync(ct);
        return ToRow(post);
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
        _db.Comments.Add(Comment.Create(row.PostId, row.AuthorId, row.Text,
            row.ParentCommentId, row.Id, row.CreatedAt));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<SocialCommentRow> Items, string? NextCursor)> ListCommentsAsync(
        Guid postId, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct)
    {
        var q = _db.Comments.Where(c => c.PostId == postId && c.DeletedAt == null);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var ca = cursorCreatedAt.Value;
            var cid = cursorId.Value;
            q = q.Where(c => c.CreatedAt > ca || (c.CreatedAt == ca && c.Id.CompareTo(cid) > 0));
        }

        var entities = await q.OrderBy(c => c.CreatedAt)
                              .ThenBy(c => c.Id)
                              .Take(limit)
                              .ToListAsync(ct);

        var rows = entities.Select(c => new SocialCommentRow(c.Id, c.PostId, c.AuthorId, c.Text, c.CreatedAt, c.ParentCommentId)).ToList();

        string? next = null;
        if (rows.Count == limit)
        {
            var last = rows[^1];
            next = CursorEncoder.Encode(last.CreatedAt, last.Id);
        }

        return (rows, next);
    }

    public Task<bool> HasReactionAsync(Guid postId, Guid userId, CancellationToken ct)
        => _db.Reactions.AnyAsync(r => r.PostId == postId && r.UserId == userId, ct);

    public async Task AddReactionAsync(SocialReactionRow row, CancellationToken ct)
    {
        _db.Reactions.Add(Reaction.Create(row.PostId, row.UserId, row.Type));
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveReactionAsync(Guid postId, Guid userId, CancellationToken ct)
    {
        var reactions = await _db.Reactions.Where(r => r.PostId == postId && r.UserId == userId).ToListAsync(ct);
        _db.Reactions.RemoveRange(reactions);
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
        return c is null ? null : new SocialCommentRow(c.Id, c.PostId, c.AuthorId, c.Text, c.CreatedAt, c.ParentCommentId);
    }

    public async Task<(bool Saved, bool Changed)> ToggleBookmarkAsync(Guid postId, Guid userId, CancellationToken ct)
    {
        var existing = await _db.PostBookmarks.FindAsync(new object[] { postId, userId }, ct);
        if (existing is null)
        {
            _db.PostBookmarks.Add(PostBookmark.Create(postId, userId));
            await _db.SaveChangesAsync(ct);
            return (true, true);
        }

        _db.PostBookmarks.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return (false, true);
    }

    public Task<bool> IsBookmarkedAsync(Guid postId, Guid userId, CancellationToken ct)
        => _db.PostBookmarks.AnyAsync(x => x.PostId == postId && x.UserId == userId, ct);

    public async Task<(bool Liked, int LikesCount)> ToggleCommentLikeAsync(Guid commentId, Guid userId, CancellationToken ct)
    {
        var existing = await _db.CommentLikes.FindAsync(new object[] { commentId, userId }, ct);
        bool liked;
        if (existing is null)
        {
            _db.CommentLikes.Add(CommentLike.Create(commentId, userId));
            liked = true;
        }
        else
        {
            _db.CommentLikes.Remove(existing);
            liked = false;
        }
        await _db.SaveChangesAsync(ct);
        var count = await _db.CommentLikes.CountAsync(x => x.CommentId == commentId, ct);
        return (liked, count);
    }

    public async Task<(int LikesCount, bool LikedByViewer)> GetCommentLikeStateAsync(Guid commentId, Guid? viewerId, CancellationToken ct)
    {
        var count = await _db.CommentLikes.CountAsync(x => x.CommentId == commentId, ct);
        var liked = viewerId.HasValue && await _db.CommentLikes.AnyAsync(x => x.CommentId == commentId && x.UserId == viewerId.Value, ct);
        return (count, liked);
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
        ImageStorageKey: p.ImageStorageKey, ImageStorageKeys: p.ImageStorageKeys?.ToList() ?? (p.ImageStorageKey is null ? new() : new() { p.ImageStorageKey }), Link: p.Link,
        Tags: p.Tags?.ToList() ?? new List<string>(),
        Visibility: p.Visibility, IsKnowledge: p.IsKnowledge,
        ReactionsCount: p.ReactionsCount, CommentsCount: p.CommentsCount,
        CreatedAt: p.CreatedAt);
}
