using Fitmate.Application.Support;
using Fitmate.Domain.Common;
using Fitmate.Domain.Support;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// R08: SupportService phải trả ValidationException (400 + errors map) chứ không phải
/// ArgumentException (500) khi input ngÆ°á»i dùng rỗng / sai enum. Ngoài ra kiểm tra
/// pagination clamp và reply/transition để bảo đảm service giữ hợp đồng với controller.
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
        string subject = "Không đăng nhập được",
        string category = "Account",
        string message = "Tôi không nhận được mã xác thực.") =>
        new(subject, category, Priority: null, message, AttachmentUrls: null);

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
        store.Added.Should().BeEmpty("validation phải chặn trước khi ghi vào store");
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
        dto.Subject.Should().Be("Không đăng nhập được");
        dto.Status.Should().Be(SupportTicketStatus.Open.ToString());
        dto.Messages.Should().HaveCount(1, "ticket mới phải có message khởi tạo");
        dto.Messages[0].Content.Should().Be("Tôi không nhận được mã xác thực.");
    }

    // ---------- Admin status transition ----------

    [Fact]
    public async Task AdminChangeStatus_InvalidStatus_ThrowsValidation_NotArgumentException()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
        var sut = new SupportService(store);

        var act = () => sut.AdminChangeStatusAsync(id, new AdminChangeStatusRequest("banana"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.HttpStatus.Should().Be(400);
        ex.Which.Code.Should().Be(ErrorCodes.Validation.FieldInvalid);
        ex.Which.Errors.Should().ContainKey("status");
        ex.Which.Message.Should().Contain("banana", "message phải nêu rõ giá trị sai");
    }

    [Fact]
    public async Task AdminChangeStatus_OutOfRangeNumericEnum_ThrowsValidation()
    {
        // "99" parse được thành enum (long) nhưng không phải giá trị định nghĩa.
        // Lưu ý: "2" là InProgress — hợp lệ, không dùng làm case âm.
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
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
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
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
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
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

        // page=0, pageSize=0 → phải clamp thành (1, 20) trước khi gá»i store.
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
        // status="99" parse được thành enum nhưng không phải giá trị định nghĩa.
        // Trước khi sửa, store nhận Status=99 → 0 dòng, admin tưởng không có ticket.
        var store = new InMemoryStore();
        var sut = new SupportService(store);

        await sut.ListAdminTicketsAsync("99", null, 1, 25, CancellationToken.None);

        store.LastAdminFilter.Status.Should().BeNull("enum không định nghĩa phải bị bá» qua, không lá»c sai");
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
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
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
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
        ticket.CloseByUser(UserId);
        store.Items[id] = ticket;
        var sut = new SupportService(store);

        var act = () => sut.UserReplyTicketAsync(id, UserId, new ReplySupportTicketRequest("Xin hỗ trợ thêm", null), CancellationToken.None);

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
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
        ticket.CloseByUser(UserId);
        store.Items[id] = ticket;
        var sut = new SupportService(store);

        var act = () => sut.AdminReplyTicketAsync(id, AdminId, new ReplySupportTicketRequest("ÄÃ£ xử lý", null), CancellationToken.None);

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
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
        var other = Guid.NewGuid();
        var sut = new SupportService(store);

        var dto = await sut.UserReplyTicketAsync(id, other, new ReplySupportTicketRequest("Xin mở", null), CancellationToken.None);

        dto.Should().BeNull("IDOR: ticket của ngÆ°á»i khác phải trả 404 chứ không phải 403");
    }

    [Fact]
    public async Task GetTicket_NonAdminOtherOwner_ReturnsNull()
    {
        var store = new InMemoryStore();
        var id = Guid.NewGuid();
        store.Items[id] = SupportTicket.Create(
            UserId, "Mất tiá»n", "Billing", SupportTicketPriority.High, "Tôi bị trừ tiá»n hai lần.");
        var sut = new SupportService(store);

        var dto = await sut.GetTicketAsync(id, Guid.NewGuid(), isAdmin: false, CancellationToken.None);

        dto.Should().BeNull();
    }
}
