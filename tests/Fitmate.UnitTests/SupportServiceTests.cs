using Fitmate.Application.Support;
using Fitmate.Domain.Common;
using Fitmate.Domain.Support;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// R08: SupportService pháº£i tráº£ ValidationException (400 + errors map) chá»© khÃ´ng pháº£i
/// ArgumentException (500) khi input ngÆ°á»i dÃ¹ng rá»—ng / sai enum. NgoÃ i ra kiá»ƒm tra
/// pagination clamp vÃ  reply/transition Ä‘á»ƒ báº£o Ä‘áº£m service giá»¯ há»£p Ä‘á»“ng vá»›i controller.
/// </summary>
public class SupportServiceTests
{
    private sealed class InMemoryStore : ISupportTicketStore
    {
        public Dictionary<Guid, SupportTicket> Items { get; } = new();
        public List<SupportTicket> Added { get; } = new();
        public (IReadOnlyList<SupportTicket> Items, int Total) LastListResult { get; set; }
            = (Array.Empty<SupportTicket>(), 0);
        public (Guid UserId, SupportTicketStatus? Status, int Page, int PageSize) LastUserFilter { get; private set; }
        public (SupportTicketStatus? Status, string? Category, int Page, int PageSize) LastAdminFilter { get; private set; }
        public Guid? LastLinkedBookingId { get; private set; }

        public Task<bool> CanLinkBookingAsync(Guid bookingId, Guid actorId, CancellationToken ct)
        {
            LastLinkedBookingId = bookingId;
            return Task.FromResult(true);
        }

        public Task<SupportTicket?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(Items.GetValueOrDefault(id));

        public Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListByUserPagedAsync(
            Guid userId, SupportTicketStatus? status, int page, int pageSize, CancellationToken ct)
        {
            LastUserFilter = (userId, status, page, pageSize);
            return Task.FromResult(LastListResult);
        }

        public Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListAdminPagedAsync(
            SupportTicketStatus? status, string? category, int page, int pageSize, CancellationToken ct)
        {
            LastAdminFilter = (status, category, page, pageSize);
            return Task.FromResult(LastListResult);
        }

        public Task AddAsync(SupportTicket ticket, CancellationToken ct)
        {
            Items[ticket.Id] = ticket;
            Added.Add(ticket);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();

    private static CreateSupportTicketRequest ValidRequest(
        string subject = "KhÃ´ng Ä‘Äƒng nháº­p Ä‘Æ°á»£c",
        string category = "Account",
        string message = "TÃ´i khÃ´ng nháº­n Ä‘Æ°á»£c mÃ£ xÃ¡c thá»±c.") =>
        new(subject, category, Priority: null, message, AttachmentUrls: null);

    [Fact]
    public async Task LinkedBooking_IsReturnedToOwnerAndAdmin()
    {
        var store = new InMemoryStore();
        var service = new SupportService(store);
        var bookingId = Guid.NewGuid();
        var request = ValidRequest() with { BookingId = bookingId };

        var created = await service.CreateTicketAsync(UserId, request, CancellationToken.None);
        var ownerView = await service.GetTicketAsync(created.Id, UserId, false, CancellationToken.None);
        var adminView = await service.GetTicketAsync(created.Id, AdminId, true, CancellationToken.None);

        store.LastLinkedBookingId.Should().Be(bookingId);
        created.BookingId.Should().Be(bookingId);
        ownerView!.BookingId.Should().Be(bookingId);
        adminView!.BookingId.Should().Be(bookingId);
    }

    [Fact]
    public async Task BookingDispute_WithoutBookingId_IsRejected()
    {
        var store = new InMemoryStore();
        var service = new SupportService(store);

        var action = () => service.CreateTicketAsync(UserId,
            ValidRequest(category: "booking_dispute"), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainKey("booking_id");
        store.Added.Should().BeEmpty();
    }

    // ---------- Create: validation ----------

    [Fact]
    public async Task CreateTicket_BlankSubject_ThrowsValidation_WithSubjectField()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var act = () => sut.CreateTicketAsync(UserId, ValidRequest(subject: "   "), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.FieldInvalid);
        ex.Which.Errors.Should().ContainKey("subject");
        store.Added.Should().BeEmpty("validation pháº£i cháº·n trÆ°á»›c khi ghi vÃ o store");
    }

    [Fact]
    public async Task CreateTicket_BlankCategory_ThrowsValidation_WithCategoryField()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var act = () => sut.CreateTicketAsync(UserId, ValidRequest(category: ""), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Errors.Should().ContainKey("category");
        store.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTicket_BlankMessage_ThrowsValidation_WithMessageField()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var act = () => sut.CreateTicketAsync(UserId, ValidRequest(message: "\t\n"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Errors.Should().ContainKey("message");
        store.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTicket_ValidRequest_PersistsAndReturnsDetail()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var dto = await sut.CreateTicketAsync(UserId, ValidRequest(), CancellationToken.None);

        store.Added.Should().ContainSingle();
        dto.Id.Should().Be(store.Added[0].Id);
        dto.UserId.Should().Be(UserId);
        dto.Subject.Should().Be("KhÃ´ng Ä‘Äƒng nháº­p Ä‘Æ°á»£c");
        dto.Status.Should().Be(SupportTicketStatus.Open.ToString());
        dto.Messages.Should().HaveCount(1, "ticket má»›i pháº£i cÃ³ message khá»Ÿi táº¡o");
        dto.Messages[0].Content.Should().Be("TÃ´i khÃ´ng nháº­n Ä‘Æ°á»£c mÃ£ xÃ¡c thá»±c.");
    }

    // ---------- Admin status transition ----------

    [Fact]
    public async Task AdminChangeStatus_InvalidStatus_ThrowsValidation_NotArgumentException()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var sut = new SupportService(store);

        var act = () => sut.AdminChangeStatusAsync(id, new AdminChangeStatusRequest("banana"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.FieldInvalid);
        ex.Which.Errors.Should().ContainKey("status");
        ex.Which.Message.Should().Contain("banana", "message pháº£i nÃªu rÃµ giÃ¡ trá»‹ sai");
    }

    [Fact]
    public async Task AdminChangeStatus_OutOfRangeNumericEnum_ThrowsValidation()
    {
        // "99" parse Ä‘Æ°á»£c thÃ nh enum (long) nhÆ°ng khÃ´ng pháº£i giÃ¡ trá»‹ Ä‘á»‹nh nghÄ©a.
        // LÆ°u Ã½: "2" lÃ  InProgress â€” há»£p lá»‡, khÃ´ng dÃ¹ng lÃ m case Ã¢m.
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var sut = new SupportService(store);

        var act = () => sut.AdminChangeStatusAsync(id, new AdminChangeStatusRequest("99"), CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<ValidationException>();
        assertion.Which.HttpStatus.Should().Be(400);
        assertion.Which.Errors.Should().ContainKey("status");
    }

    [Fact]
    public async Task AdminChangeStatus_NumericValueMatchingDefinedEnum_IsAccepted()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var sut = new SupportService(store);

        var dto = await sut.AdminChangeStatusAsync(id, new AdminChangeStatusRequest("2"), CancellationToken.None);

        dto!.Status.Should().Be(SupportTicketStatus.InProgress.ToString());
    }

    [Fact]
    public async Task AdminChangeStatus_ValidStatus_PersistsTransition()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var sut = new SupportService(store);

        var dto = await sut.AdminChangeStatusAsync(
            id, new AdminChangeStatusRequest("Resolved"), CancellationToken.None);

        dto!.Status.Should().Be(SupportTicketStatus.Resolved.ToString());
        store.Items[id].Status.Should().Be(SupportTicketStatus.Resolved);
    }

    [Fact]
    public async Task AdminChangeStatus_UnknownTicket_ReturnsNull()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var dto = await sut.AdminChangeStatusAsync(Guid.NewGuid(), new AdminChangeStatusRequest("Closed"), CancellationToken.None);

        dto.Should().BeNull();
    }

    // ---------- Pagination clamp + status filter ----------

    [Fact]
    public async Task ListAdmin_ClampsPagination_BeforeStoreCall()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        // page=0, pageSize=0 â†’ pháº£i clamp thÃ nh (1, 20) trÆ°á»›c khi gá»i store.
        var result = await sut.ListAdminTicketsAsync(null, null, 0, 0, CancellationToken.None);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        store.LastAdminFilter.Page.Should().Be(1);
        store.LastAdminFilter.PageSize.Should().Be(20);
        store.LastAdminFilter.Status.Should().BeNull();
        store.LastAdminFilter.Category.Should().BeNull();
    }

    [Fact]
    public async Task ListAdmin_PageSizeAboveMax_IsClampedTo100()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var result = await sut.ListAdminTicketsAsync(null, null, 1, 5000, CancellationToken.None);

        result.PageSize.Should().Be(100);
        result.Page.Should().Be(1);
    }

    [Fact]
    public async Task ListAdmin_EmptyResult_ReturnsEmptyItemsNotNull()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        var result = await sut.ListAdminTicketsAsync(null, null, 1, 25, CancellationToken.None);

        result.Items.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task ListAdmin_UndefinedEnumStatus_IsIgnored_NotSilentlyEmptyList()
    {
        // status="99" parse Ä‘Æ°á»£c thÃ nh enum nhÆ°ng khÃ´ng pháº£i giÃ¡ trá»‹ Ä‘á»‹nh nghÄ©a.
        // TrÆ°á»›c khi sá»­a, store nháº­n Status=99 â†’ 0 dÃ²ng, admin tÆ°á»Ÿng khÃ´ng cÃ³ ticket.
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        await sut.ListAdminTicketsAsync("99", null, 1, 25, CancellationToken.None);

        store.LastAdminFilter.Status.Should().BeNull("enum khÃ´ng Ä‘á»‹nh nghÄ©a pháº£i bá»‹ bá» qua, khÃ´ng lá»c sai");
    }

    [Fact]
    public async Task ListMy_ValidStatus_IsForwardedToStore()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        await sut.ListMyTicketsAsync(UserId, "resolved", 2, 50, CancellationToken.None);

        store.LastUserFilter.UserId.Should().Be(UserId);
        store.LastUserFilter.Status.Should().Be(SupportTicketStatus.Resolved);
        store.LastUserFilter.Page.Should().Be(2);
        store.LastUserFilter.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task ListMy_UndefinedEnumStatus_IsIgnored()
    {
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        await sut.ListMyTicketsAsync(UserId, "7", 1, 20, CancellationToken.None);

        store.LastUserFilter.Status.Should().BeNull();
    }

    // ---------- Reply paths ----------

    [Fact]
    public async Task UserReply_BlankMessage_ThrowsValidation()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var sut = new SupportService(store);

        var act = () => sut.UserReplyTicketAsync(id, UserId, new ReplySupportTicketRequest("  ", null), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Errors.Should().ContainKey("message");
    }

    [Fact]
    public async Task UserReply_ClosedTicket_ThrowsConflict_NotInvalidOperation()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        var ticket = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        ticket.CloseByUser(UserId);
        store.Items[id] = ticket;
        var sut = new SupportService(store);

        var act = () => sut.UserReplyTicketAsync(id, UserId, new ReplySupportTicketRequest("Xin há»— trá»£ thÃªm", null), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.HttpStatus.Should().Be(409);
        ex.Which.Code.Should().Be(ErrorCodes.Support.TicketClosed);
    }

    [Fact]
    public async Task AdminReply_ClosedTicket_ThrowsConflict_NotInvalidOperation()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        var ticket = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        ticket.CloseByUser(UserId);
        store.Items[id] = ticket;
        var sut = new SupportService(store);

        var act = () => sut.AdminReplyTicketAsync(id, AdminId, new ReplySupportTicketRequest("ÄÃ£ xá»­ lÃ½", null), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.HttpStatus.Should().Be(409);
        ex.Which.Code.Should().Be(ErrorCodes.Support.TicketClosed);
    }

    [Fact]
    public async Task UserReply_OtherOwnerTicket_ReturnsNull_NotLeak()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var other = Guid.NewGuid();
        var sut = new SupportService(store);

        var dto = await sut.UserReplyTicketAsync(id, other, new ReplySupportTicketRequest("Xin má»Ÿ", null), CancellationToken.None);

        dto.Should().BeNull("IDOR: ticket cá»§a ngÆ°á»i khÃ¡c pháº£i tráº£ 404 chá»© khÃ´ng pháº£i 403");
    }

    [Fact]
    public async Task GetTicket_NonAdminOtherOwner_ReturnsNull()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Máº¥t tiá»n", "Billing", SupportTicketPriority.High, "TÃ´i bá»‹ trá»« tiá»n hai láº§n.");
        var sut = new SupportService(store);

        var dto = await sut.GetTicketAsync(id, Guid.NewGuid(), isAdmin: false, CancellationToken.None);

        dto.Should().BeNull();
    }
}
