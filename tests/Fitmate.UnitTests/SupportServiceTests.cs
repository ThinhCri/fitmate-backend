using Fitmate.Application.Support;
using Fitmate.Domain.Support;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class SupportServiceTests
{
    private sealed class InMemorySupportTicketStore : ISupportTicketStore
    {
        public Dictionary<Guid, SupportTicket> Items { get; } = new();

        public Task<SupportTicket?> FindByIdAsync(Guid id, CancellationToken ct)
        {
            return Task.FromResult(Items.GetValueOrDefault(id));
        }

        public Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListByUserPagedAsync(
            Guid userId,
            SupportTicketStatus? status,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            var query = Items.Values.Where(x => x.UserId == userId);
            if (status.HasValue)
                query = query.Where(x => x.Status == status.Value);

            var list = query.OrderByDescending(x => x.CreatedAt).ToList();
            var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<SupportTicket>)paged, list.Count));
        }

        public Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListAdminPagedAsync(
            SupportTicketStatus? status,
            string? category,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            var query = Items.Values.AsQueryable();
            if (status.HasValue)
                query = query.Where(x => x.Status == status.Value);
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(x => x.Category == category.Trim().ToLowerInvariant());

            var list = query.OrderByDescending(x => x.CreatedAt).ToList();
            var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<SupportTicket>)paged, list.Count));
        }

        public Task AddAsync(SupportTicket ticket, CancellationToken ct)
        {
            Items[ticket.Id] = ticket;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task CreateTicket_WithValidData_CreatesTicketInOpenStatus()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();

        var req = new CreateSupportTicketRequest(
            Subject: "Payment issue",
            Category: "billing",
            Priority: "High",
            Message: "I was double charged.",
            AttachmentUrls: new List<string> { "https://cdn.fitmate.vn/receipt.png" });

        var result = await svc.CreateTicketAsync(userId, req, CancellationToken.None);

        result.Should().NotBeNull();
        result.UserId.Should().Be(userId);
        result.Subject.Should().Be("Payment issue");
        result.Category.Should().Be("billing");
        result.Priority.Should().Be("High");
        result.Status.Should().Be("Open");
        result.Messages.Should().HaveCount(1);
        result.Messages[0].Content.Should().Be("I was double charged.");
        result.Messages[0].IsAdmin.Should().BeFalse();
        result.Messages[0].AttachmentUrls.Should().Contain("https://cdn.fitmate.vn/receipt.png");
    }

    [Fact]
    public async Task CreateTicket_WithMissingSubject_ThrowsArgumentException()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);

        var req = new CreateSupportTicketRequest("", "billing", "Normal", "Msg", null);

        var act = () => svc.CreateTicketAsync(Guid.NewGuid(), req, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetTicket_ByOwner_ReturnsDetail()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var ownerId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            ownerId,
            new CreateSupportTicketRequest("Subj", "tech", "Normal", "Help", null),
            CancellationToken.None);

        var fetched = await svc.GetTicketAsync(created.Id, ownerId, isAdmin: false, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetTicket_ByNonOwner_ReturnsNull_ForIdorProtection()
    {
        // DoD: "Ticket người khác 404"
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            ownerId,
            new CreateSupportTicketRequest("Private Issue", "account", "Urgent", "Secret", null),
            CancellationToken.None);

        var fetched = await svc.GetTicketAsync(created.Id, strangerId, isAdmin: false, CancellationToken.None);

        fetched.Should().BeNull(); // Controller will map null to 404 Not Found!
    }

    [Fact]
    public async Task GetTicket_ByAdmin_ReturnsDetailEvenIfNonOwner()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            ownerId,
            new CreateSupportTicketRequest("Subj", "tech", "Normal", "Msg", null),
            CancellationToken.None);

        var fetched = await svc.GetTicketAsync(created.Id, adminId, isAdmin: true, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task UserReplyTicket_AppendsMessageAndSetsStatusToOpen()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            userId,
            new CreateSupportTicketRequest("Question", "general", "Low", "First msg", null),
            CancellationToken.None);

        var replied = await svc.UserReplyTicketAsync(
            created.Id,
            userId,
            new ReplySupportTicketRequest("Second msg", null),
            CancellationToken.None);

        replied.Should().NotBeNull();
        replied!.Messages.Should().HaveCount(2);
        replied.Messages[1].Content.Should().Be("Second msg");
        replied.Status.Should().Be("Open");
    }

    [Fact]
    public async Task AdminReplyTicket_AppendsAdminMessageAndSetsStatusToWaitingForUser()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            userId,
            new CreateSupportTicketRequest("Question", "general", "Low", "First msg", null),
            CancellationToken.None);

        var replied = await svc.AdminReplyTicketAsync(
            created.Id,
            adminId,
            new ReplySupportTicketRequest("Admin response", null),
            CancellationToken.None);

        replied.Should().NotBeNull();
        replied!.Messages.Should().HaveCount(2);
        replied.Messages[1].IsAdmin.Should().BeTrue();
        replied.Messages[1].Content.Should().Be("Admin response");
        replied.Status.Should().Be("WaitingForUser");
    }

    [Fact]
    public async Task UserCloseTicket_ByOwner_SetsClosedStatus()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            userId,
            new CreateSupportTicketRequest("Issue", "general", "Low", "Help", null),
            CancellationToken.None);

        var success = await svc.UserCloseTicketAsync(created.Id, userId, CancellationToken.None);
        success.Should().BeTrue();

        var fetched = await svc.GetTicketAsync(created.Id, userId, isAdmin: false, CancellationToken.None);
        fetched!.Status.Should().Be("Closed");
        fetched.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReplyTicket_OnClosedTicket_ThrowsInvalidOperationException()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            userId,
            new CreateSupportTicketRequest("Issue", "general", "Low", "Help", null),
            CancellationToken.None);

        await svc.UserCloseTicketAsync(created.Id, userId, CancellationToken.None);

        var act = () => svc.UserReplyTicketAsync(
            created.Id,
            userId,
            new ReplySupportTicketRequest("Reply to closed", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AdminChangeStatus_WithValidStatus_UpdatesStatusAndTimestamp()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();

        var created = await svc.CreateTicketAsync(
            userId,
            new CreateSupportTicketRequest("Issue", "technical", "Normal", "Bug report", null),
            CancellationToken.None);

        var updated = await svc.AdminChangeStatusAsync(
            created.Id,
            new AdminChangeStatusRequest("Resolved"),
            CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.Status.Should().Be("Resolved");
        updated.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ListMyTickets_WithStatusFilter_ReturnsFilteredTickets()
    {
        var store = new InMemorySupportTicketStore();
        var svc = new SupportService(store);
        var userId = Guid.NewGuid();

        var t1 = await svc.CreateTicketAsync(userId, new CreateSupportTicketRequest("T1", "cat", "Low", "M1", null), CancellationToken.None);
        var t2 = await svc.CreateTicketAsync(userId, new CreateSupportTicketRequest("T2", "cat", "Low", "M2", null), CancellationToken.None);
        await svc.UserCloseTicketAsync(t2.Id, userId, CancellationToken.None);

        var openOnly = await svc.ListMyTicketsAsync(userId, "Open", 1, 10, CancellationToken.None);
        openOnly.Total.Should().Be(1);
        openOnly.Items.Should().ContainSingle(x => x.Id == t1.Id);

        var closedOnly = await svc.ListMyTicketsAsync(userId, "Closed", 1, 10, CancellationToken.None);
        closedOnly.Total.Should().Be(1);
        closedOnly.Items.Should().ContainSingle(x => x.Id == t2.Id);
    }
}

