using Fitmate.Application.Auth;
using Fitmate.Application.Moderation;
using Fitmate.Application.Social;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Moderation;
using Fitmate.Domain.Social;
using Fitmate.Domain.Trainers;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-180 + BE-211: SocialService — post feed pagination + create + visibility filter + comment + reaction toggle + report.
/// </summary>
public class SocialServiceTests
{
    private sealed class AllowAllAuthz : IAuthorizationGuard
    {
        public Task EnsureRoleAsync(Guid userId, string role, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureAnyRoleAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureOwnerAsync(string r, Guid u, Guid o, CancellationToken ct) => Task.CompletedTask;
        public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct) => Task.FromResult<string?>("Member");
        public Task<bool> HasRoleAsync(Guid userId, string role, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class InMemoryCertificateStore : ICertificateStore
    {
        public Dictionary<Guid, Certificate> Certs { get; } = new();
        public Task<Certificate?> FindByIdentityAsync(Guid ptUserId, string title, string issuedBy, string code, CancellationToken ct) => Task.FromResult<Certificate?>(null);
        public Task<Certificate?> FindByIdAsync(Guid certId, CancellationToken ct) => Task.FromResult<Certificate?>(Certs.GetValueOrDefault(certId));
        public Task<IReadOnlyList<Certificate>> ListByPtAsync(Guid ptUserId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Certificate>>(new List<Certificate>());
        public Task<(IReadOnlyList<Certificate> Items, int Total)> ListByPtPagedAsync(Guid ptUserId, int page, int pageSize, CancellationToken ct) => Task.FromResult<(IReadOnlyList<Certificate>, int)>((new List<Certificate>(), 0));
        public Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingPagedAsync(int page, int pageSize, CancellationToken ct) => Task.FromResult<(IReadOnlyList<Certificate>, int)>((new List<Certificate>(), 0));
        public Task AddAsync(Certificate cert, CancellationToken ct) { Certs[cert.Id] = cert; return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class InMemorySocialStore : ISocialStore
    {
        public List<SocialPostRow> Posts { get; } = new();
        public List<SocialCommentRow> Comments { get; } = new();
        public List<SocialReactionRow> Reactions { get; } = new();
        public List<(Guid ReporterId, ReportTargetType Kind, Guid TargetId, string Reason, string? Details, string? IdempotencyKey)> Reports { get; } = new();
        public Dictionary<Guid, SocialAuthorRow> Authors { get; } = new();

        public Task<(IReadOnlyList<SocialPostRow> Items, string? NextCursor)> ListPostsAsync(
            Guid? viewerId, FeedScope scope, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct)
        {
            IEnumerable<SocialPostRow> q = Posts;
            if (scope == FeedScope.Public)
                q = q.Where(p => p.Visibility == PostVisibility.Public);
            else
                q = q.Where(p => p.Visibility == PostVisibility.Public || (viewerId.HasValue && p.AuthorId == viewerId.Value));
            if (cursorCreatedAt.HasValue && cursorId.HasValue)
                q = q.Where(p => p.CreatedAt < cursorCreatedAt.Value
                              || (p.CreatedAt == cursorCreatedAt.Value && p.Id.CompareTo(cursorId.Value) < 0));
            var ordered = q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Take(limit).ToList();
            return Task.FromResult<(IReadOnlyList<SocialPostRow>, string?)>((ordered, null));
        }

        public Task<SocialPostRow?> GetPostByIdAsync(Guid postId, CancellationToken ct)
            => Task.FromResult<SocialPostRow?>(Posts.FirstOrDefault(p => p.Id == postId));
        public Task<SocialPostRow?> GetPostWithAuthorAsync(Guid postId, CancellationToken ct) => GetPostByIdAsync(postId, ct);

        public Task AddPostAsync(SocialPostRow row, CancellationToken ct) { Posts.Add(row); return Task.CompletedTask; }
        public Task<SocialPostRow?> UpdatePostVisibilityAsync(Guid postId, PostVisibility visibility, CancellationToken ct)
        {
            var index = Posts.FindIndex(x => x.Id == postId);
            if (index < 0) return Task.FromResult<SocialPostRow?>(null);
            Posts[index] = Posts[index] with { Visibility = visibility };
            return Task.FromResult<SocialPostRow?>(Posts[index]);
        }
        public Task<bool> RemovePostAsync(Guid postId, CancellationToken ct)
        {
            var p = Posts.FirstOrDefault(x => x.Id == postId);
            if (p is null) return Task.FromResult(false);
            Posts.Remove(p);
            return Task.FromResult(true);
        }
        public Task<bool> PostExistsAsync(Guid postId, CancellationToken ct)
            => Task.FromResult(Posts.Any(p => p.Id == postId));
        public Task AddCommentAsync(SocialCommentRow row, CancellationToken ct) { Comments.Add(row); return Task.CompletedTask; }
        public Task<bool> HasReactionAsync(Guid postId, Guid userId, CancellationToken ct)
            => Task.FromResult(Reactions.Any(r => r.PostId == postId && r.UserId == userId));
        public Task AddReactionAsync(SocialReactionRow row, CancellationToken ct)
        {
            Reactions.Add(row);
            return Task.CompletedTask;
        }
        public Task<int> ChangePostReactionsAsync(Guid postId, int delta, CancellationToken ct)
        {
            var idx = Posts.FindIndex(x => x.Id == postId);
            if (idx < 0) return Task.FromResult(0);
            Posts[idx] = Posts[idx] with { ReactionsCount = Math.Max(0, Posts[idx].ReactionsCount + delta) };
            return Task.FromResult(Posts[idx].ReactionsCount);
        }
        public Task<int> ChangePostCommentsAsync(Guid postId, int delta, CancellationToken ct)
        {
            var idx = Posts.FindIndex(x => x.Id == postId);
            if (idx < 0) return Task.FromResult(0);
            Posts[idx] = Posts[idx] with { CommentsCount = Math.Max(0, Posts[idx].CommentsCount + delta) };
            return Task.FromResult(Posts[idx].CommentsCount);
        }
        public Task<bool> TargetExistsAsync(ReportTargetType kind, Guid targetId, CancellationToken ct) => Task.FromResult(kind switch
        {
            ReportTargetType.Post => Posts.Any(p => p.Id == targetId),
            ReportTargetType.Comment => Comments.Any(c => c.Id == targetId),
            ReportTargetType.User => Authors.ContainsKey(targetId),
            ReportTargetType.Certificate => false, // certificate handled separately in service
            _ => false
        });
        public Task<Guid> AddReportAsync(Guid reporterId, ReportTargetType kind, Guid targetId, string reason, string? details, string? idempotencyKey, CancellationToken ct)
        {
            Reports.Add((reporterId, kind, targetId, reason, details, idempotencyKey));
            return Task.FromResult(Guid.NewGuid());
        }
        public Task<(IReadOnlyList<SocialCommentRow> Items, string? NextCursor)> ListCommentsAsync(
            Guid postId, DateTime? cursorCreatedAt, Guid? cursorId, int limit, CancellationToken ct)
        {
            var q = Comments.Where(c => c.PostId == postId);
            if (cursorCreatedAt.HasValue && cursorId.HasValue)
                q = q.Where(c => c.CreatedAt > cursorCreatedAt.Value || (c.CreatedAt == cursorCreatedAt.Value && c.Id.CompareTo(cursorId.Value) > 0));
            var page = q.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).Take(limit).ToList();
            return Task.FromResult<(IReadOnlyList<SocialCommentRow>, string?)>((page, null));
        }

        public Task<SocialCommentRow?> GetCommentAsync(Guid commentId, CancellationToken ct)
            => Task.FromResult<SocialCommentRow?>(Comments.FirstOrDefault(c => c.Id == commentId));
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<ReportRow?> FindOpenReportAsync(Guid reporterId, ReportTargetType targetType, Guid targetId, CancellationToken ct) => Task.FromResult<ReportRow?>(null);
        public Task<ReportRow?> FindOpenReportByKeyAsync(Guid reporterId, string idempotencyKey, CancellationToken ct) => Task.FromResult<ReportRow?>(null);
        public Task<SocialAuthorRow?> GetAuthorAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<SocialAuthorRow?>(Authors.GetValueOrDefault(userId));
    }

    private sealed class StubBlockStore : IBlockStore
    {
        public HashSet<(Guid, Guid)> Blocks { get; } = new();
        public Task<bool> IsBlockedEitherWayAsync(Guid a, Guid b, CancellationToken ct) => Task.FromResult(Blocks.Contains((a, b)) || Blocks.Contains((b, a)));
        public Task<bool> BlockExistsAsync(Guid b, Guid d, CancellationToken ct) => Task.FromResult(Blocks.Contains((b, d)));
        public Task AddBlockAsync(BlockedUser x, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> RemoveBlockAsync(Guid b, Guid d, CancellationToken ct) => Task.FromResult(Blocks.Remove((b, d)));
        public Task<(IReadOnlyList<Guid>, string?)> ListBlockedIdsAsync(Guid b, DateTime? c, Guid? i, int l, CancellationToken ct) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static SocialService NewService(InMemorySocialStore store, InMemoryCertificateStore certStore, StubBlockStore? blockStore = null)
        => new(store, certStore, new AllowAllAuthz(), new StubFriendService(), blockStore ?? new StubBlockStore());

    /// <summary>BE-181: minimal stub cho FriendService — IsFriendAsync trả false (giống stub cũ) để SocialService test không phụ thuộc friend state.</summary>
    private sealed class StubFriendService : IFriendService
    {
        public Task<FriendRequestView> SendRequestAsync(Guid s, Guid r, CancellationToken ct) => throw new NotImplementedException();
        public Task AcceptRequestAsync(Guid a, Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task RejectRequestAsync(Guid a, Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task CancelRequestAsync(Guid a, Guid id, CancellationToken ct) => throw new NotImplementedException();
        public Task UnfriendAsync(Guid a, Guid o, CancellationToken ct) => throw new NotImplementedException();
        public Task<FriendPage> ListFriendsAsync(Guid u, string? c, int l, CancellationToken ct) => throw new NotImplementedException();
        public Task<bool> IsFriendAsync(Guid a, Guid b, CancellationToken ct) => Task.FromResult(false);
        public Task<FriendRequestStatusView> GetRelationStatusAsync(Guid v, Guid o, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<FriendRequestDetailedView>> ListPendingRequestsAsync(Guid u, string t, CancellationToken ct) => throw new NotImplementedException();
    }

    private static SocialPostRow NewPost(Guid author, PostVisibility vis, string content = "hi", DateTime? at = null, int reactions = 0, int comments = 0)
        => new(Guid.NewGuid(), author, content, null, Array.Empty<string>(), null, new List<string>(), vis, false, reactions, comments, at ?? DateTime.UtcNow);

    [Fact]
    public async Task CreatePost_Validates_Required_Content()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.CreatePostAsync(Guid.NewGuid(),
            new PostInput("", null, Array.Empty<string>(), null, new List<string>(), PostVisibility.Public, false), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.RequiredField);
    }

    [Fact]
    public async Task CreatePost_Rejects_TooLong_Content()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.CreatePostAsync(Guid.NewGuid(),
            new PostInput(new string('x', 5001), null, Array.Empty<string>(), null, new List<string>(), PostVisibility.Public, false), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooLong);
    }

    [Fact]
    public async Task CreatePost_Rejects_TooMany_Tags()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var tags = Enumerable.Range(0, 11).Select(i => $"t{i}").ToList();
        var act = () => s.CreatePostAsync(Guid.NewGuid(),
            new PostInput("ok", null, Array.Empty<string>(), null, tags, PostVisibility.Public, false), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooMany);
    }

    [Fact]
    public async Task CreatePost_PersistsAndReturns()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Authors[author] = new SocialAuthorRow(author, "A", null);
        var s = NewService(store, new InMemoryCertificateStore());

        var post = await s.CreatePostAsync(author,
            new PostInput("hello", null, Array.Empty<string>(), null, new List<string> { "fit" }, PostVisibility.Public, false), CancellationToken.None);

        post.Content.Should().Be("hello");
        post.Tags.Should().ContainSingle("fit");
        post.ReactionsCount.Should().Be(0);
        store.Posts.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListPosts_PublicScope_FiltersOutPrivate()
    {
        var store = new InMemorySocialStore();
        store.Posts.Add(NewPost(Guid.NewGuid(), PostVisibility.Public));
        store.Posts.Add(NewPost(Guid.NewGuid(), PostVisibility.Private));
        var page = await NewService(store, new InMemoryCertificateStore()).ListPostsAsync(viewerId: null, FeedScope.Public, cursor: null, limit: 10, CancellationToken.None);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListPosts_FriendsScope_OnlyForViewer()
    {
        var store = new InMemorySocialStore();
        var viewer = Guid.NewGuid();
        var other = Guid.NewGuid();
        store.Posts.Add(NewPost(viewer, PostVisibility.Friends));
        store.Posts.Add(NewPost(other, PostVisibility.Friends));
        store.Posts.Add(NewPost(other, PostVisibility.Public));

        var page = await NewService(store, new InMemoryCertificateStore()).ListPostsAsync(viewer, FeedScope.Friends, null, 10, CancellationToken.None);
        // Viewer thấy: chính mình friends + other's public = 2
        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListPosts_LimitClampedToMax()
    {
        var store = new InMemorySocialStore();
        for (var i = 0; i < 200; i++) store.Posts.Add(NewPost(Guid.NewGuid(), PostVisibility.Public));
        var page = await NewService(store, new InMemoryCertificateStore()).ListPostsAsync(null, FeedScope.Public, null, 1000, CancellationToken.None);
        page.Items.Should().HaveCount(100); // MaxLimit
    }

    [Fact]
    public async Task GetPost_Private_NotVisible_ToOthers()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid(); var viewer = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Private));
        var act = () => NewService(store, new InMemoryCertificateStore()).GetPostAsync(viewer, store.Posts[0].Id, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetPost_AuthorCanSeeOwnPrivate()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Authors[author] = new SocialAuthorRow(author, "A", null);
        store.Posts.Add(NewPost(author, PostVisibility.Private, "secret"));
        var p = await NewService(store, new InMemoryCertificateStore()).GetPostAsync(author, store.Posts[0].Id, CancellationToken.None);
        p.Content.Should().Be("secret");
    }

    [Fact]
    public async Task DeletePost_Author_Allowed()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        await NewService(store, new InMemoryCertificateStore()).DeletePostAsync(author, isAdmin: false, store.Posts[0].Id, CancellationToken.None);
        store.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePost_OtherUser_Forbidden()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid(); var other = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        var act = () => NewService(store, new InMemoryCertificateStore()).DeletePostAsync(other, false, store.Posts[0].Id, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DeletePost_Admin_AnyPost()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        await NewService(store, new InMemoryCertificateStore()).DeletePostAsync(Guid.NewGuid(), isAdmin: true, store.Posts[0].Id, CancellationToken.None);
        store.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task AddComment_TooLong_Rejected()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        var act = () => NewService(store, new InMemoryCertificateStore()).AddCommentAsync(Guid.NewGuid(), store.Posts[0].Id, new string('x', 1001), CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooLong);
    }

    [Fact]
    public async Task AddComment_PostNotFound_NotFound()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.AddCommentAsync(Guid.NewGuid(), Guid.NewGuid(), "hi", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AddComment_IncrementsCounter()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        await NewService(store, new InMemoryCertificateStore()).AddCommentAsync(author, store.Posts[0].Id, "nice", CancellationToken.None);
        store.Posts[0].CommentsCount.Should().Be(1);
    }

    [Fact]
    public async Task AddComment_WithParent_PersistsThreadRelationship()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        var root = await NewService(store, new InMemoryCertificateStore())
            .AddCommentAsync(author, store.Posts[0].Id, "root", null, CancellationToken.None);
        var reply = await NewService(store, new InMemoryCertificateStore())
            .AddCommentAsync(author, store.Posts[0].Id, "reply", root.Id, CancellationToken.None);
        reply.ParentCommentId.Should().Be(root.Id);
    }

    [Fact]
    public async Task UpdatePostVisibility_OwnerOnly()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        var service = NewService(store, new InMemoryCertificateStore());

        var updated = await service.UpdatePostVisibilityAsync(author, store.Posts[0].Id, PostVisibility.Private, CancellationToken.None);
        updated.Visibility.Should().Be("private");
        var forbidden = () => service.UpdatePostVisibilityAsync(Guid.NewGuid(), store.Posts[0].Id, PostVisibility.Public, CancellationToken.None);
        await forbidden.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ToggleReaction_FirstTime_Succeeds()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid(); var reactor = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        await NewService(store, new InMemoryCertificateStore()).ToggleReactionAsync(reactor, store.Posts[0].Id, ReactionType.Heart, CancellationToken.None);
        store.Reactions.Should().HaveCount(1);
        store.Posts[0].ReactionsCount.Should().Be(1);
    }

    [Fact]
    public async Task ToggleReaction_SecondTime_Conflict()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid(); var reactor = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        await NewService(store, new InMemoryCertificateStore()).ToggleReactionAsync(reactor, store.Posts[0].Id, ReactionType.Heart, CancellationToken.None);
        var act = () => NewService(store, new InMemoryCertificateStore()).ToggleReactionAsync(reactor, store.Posts[0].Id, ReactionType.Flame, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == ErrorCodes.Social.AlreadyReacted);
    }

    [Fact]
    public async Task Report_TooShort_Rejected()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.CreateReportAsync(Guid.NewGuid(),
            new ReportInputDto(ReportTargetType.Post, Guid.NewGuid(), "abc", null, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Validation.TooShort);
    }

    [Fact]
    public async Task Report_TargetNotFound_Rejected()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.CreateReportAsync(Guid.NewGuid(),
            new ReportInputDto(ReportTargetType.Post, Guid.NewGuid(), "spam content here", null, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Social.ReportTargetNotFound);
    }

    [Fact]
    public async Task Report_Valid_ReturnsId()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        store.Posts.Add(NewPost(author, PostVisibility.Public));
        var id = await NewService(store, new InMemoryCertificateStore()).CreateReportAsync(Guid.NewGuid(),
            new ReportInputDto(ReportTargetType.Post, store.Posts[0].Id, "spam content", "details", null), null, CancellationToken.None);
        id.Should().NotBe(Guid.Empty);
        store.Reports.Should().HaveCount(1);
    }

    [Fact]
    public async Task AnonymousViewer_Rejected()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.GetPostAsync(Guid.Empty, Guid.NewGuid(), CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // === BE-211 tests ===

    [Fact]
    public async Task Report_Certificate_NotFound()
    {
        var s = NewService(new InMemorySocialStore(), new InMemoryCertificateStore());
        var act = () => s.CreateReportAsync(Guid.NewGuid(),
            new ReportInputDto(ReportTargetType.Certificate, Guid.NewGuid(), "fake cert", null, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Social.ReportTargetNotFound);
    }

    [Fact]
    public async Task Report_Certificate_Pending_Rejected()
    {
        var certStore = new InMemoryCertificateStore();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", DateOnly.FromDateTime(DateTime.UtcNow), null);
        certStore.Certs[cert.Id] = cert;
        var s = NewService(new InMemorySocialStore(), certStore);
        var act = () => s.CreateReportAsync(Guid.NewGuid(),
            new ReportInputDto(ReportTargetType.Certificate, cert.Id, "fake cert", null, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>().Where(e => e.Code == ErrorCodes.Moderation.CertificateNotReportable);
    }

    [Fact]
    public async Task Report_Certificate_Approved_Succeeds()
    {
        var certStore = new InMemoryCertificateStore();
        var admin = Guid.NewGuid();
        var cert = Certificate.Submit(Guid.NewGuid(), "T", "I", "C", DateOnly.FromDateTime(DateTime.UtcNow), null);
        cert.Approve(admin);
        certStore.Certs[cert.Id] = cert;
        var store = new InMemorySocialStore();
        var s = NewService(store, certStore);
        var id = await s.CreateReportAsync(Guid.NewGuid(),
            new ReportInputDto(ReportTargetType.Certificate, cert.Id, "fake cert", null, null), null, CancellationToken.None);
        id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task ListComments_ReturnsComments_WithAuthor()
    {
        var store = new InMemorySocialStore();
        var author = Guid.NewGuid();
        var commenter = Guid.NewGuid();
        store.Authors[commenter] = new SocialAuthorRow(commenter, "Commenter", null);
        var post = NewPost(author, PostVisibility.Public);
        store.Posts.Add(post);
        store.Comments.Add(new SocialCommentRow(Guid.NewGuid(), post.Id, commenter, "great post", DateTime.UtcNow));

        var s = NewService(store, new InMemoryCertificateStore());
        var page = await s.ListCommentsAsync(viewerId: null, post.Id, cursor: null, limit: 10, CancellationToken.None);

        page.Items.Should().HaveCount(1);
        page.Items[0].Text.Should().Be("great post");
        page.Items[0].Author.DisplayName.Should().Be("Commenter");
    }

    [Fact]
    public async Task ListComments_WhenAuthorBlocked_ThrowsNotFound()
    {
        var store = new InMemorySocialStore();
        var blockStore = new StubBlockStore();
        var author = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        blockStore.Blocks.Add((viewer, author));
        var post = NewPost(author, PostVisibility.Public);
        store.Posts.Add(post);

        var s = NewService(store, new InMemoryCertificateStore(), blockStore);
        var act = () => s.ListCommentsAsync(viewer, post.Id, cursor: null, limit: 10, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Social.PostNotFound);
    }

    [Fact]
    public async Task GetPost_WhenAuthorBlocked_ThrowsNotFound()
    {
        var store = new InMemorySocialStore();
        var blockStore = new StubBlockStore();
        var author = Guid.NewGuid();
        var viewer = Guid.NewGuid();
        blockStore.Blocks.Add((author, viewer)); // author blocked viewer
        var post = NewPost(author, PostVisibility.Public);
        store.Posts.Add(post);

        var s = NewService(store, new InMemoryCertificateStore(), blockStore);
        var act = () => s.GetPostAsync(viewer, post.Id, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>().Where(e => e.Code == ErrorCodes.Social.PostNotFound);
    }

    [Fact]
    public async Task AddComment_WhenAuthorBlocked_ThrowsForbidden()
    {
        var store = new InMemorySocialStore();
        var blockStore = new StubBlockStore();
        var author = Guid.NewGuid();
        var commenter = Guid.NewGuid();
        blockStore.Blocks.Add((author, commenter));
        var post = NewPost(author, PostVisibility.Public);
        store.Posts.Add(post);

        var s = NewService(store, new InMemoryCertificateStore(), blockStore);
        var act = () => s.AddCommentAsync(commenter, post.Id, "hello", CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == ErrorCodes.Social.RecipientBlocked);
    }

    [Fact]
    public async Task ToggleReaction_WhenAuthorBlocked_ThrowsForbidden()
    {
        var store = new InMemorySocialStore();
        var blockStore = new StubBlockStore();
        var author = Guid.NewGuid();
        var reactor = Guid.NewGuid();
        blockStore.Blocks.Add((reactor, author));
        var post = NewPost(author, PostVisibility.Public);
        store.Posts.Add(post);

        var s = NewService(store, new InMemoryCertificateStore(), blockStore);
        var act = () => s.ToggleReactionAsync(reactor, post.Id, ReactionType.Heart, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == ErrorCodes.Social.RecipientBlocked);
    }
}
