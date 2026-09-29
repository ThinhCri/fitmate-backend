using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Support;

namespace Fitmate.Application.Support;

public class SupportService : ISupportService
{
    private readonly ISupportTicketStore _store;
    private readonly IOutboxDispatcher? _outbox;

    public SupportService(ISupportTicketStore store, IOutboxDispatcher? outbox = null)
    {
        _store = store;
        _outbox = outbox;
    }

    public async Task<SupportTicketDetailDto> CreateTicketAsync(
        Guid userId,
        CreateSupportTicketRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Subject))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Subject is required.",
                new Dictionary<string, string[]> { ["subject"] = new[] { "required" } });
        if (string.IsNullOrWhiteSpace(request.Category))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Category is required.",
                new Dictionary<string, string[]> { ["category"] = new[] { "required" } });
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Initial message is required.",
                new Dictionary<string, string[]> { ["message"] = new[] { "required" } });

        var priority = SupportTicketPriority.Normal;
        if (!string.IsNullOrWhiteSpace(request.Priority) &&
            Enum.TryParse<SupportTicketPriority>(request.Priority, true, out var parsedPriority))
        {
            priority = parsedPriority;
        }

        var ticket = SupportTicket.Create(
            userId,
            request.Subject,
            request.Category,
            priority,
            request.Message,
            request.AttachmentUrls);

        if (request.BookingId.HasValue)
        {
            if (!await _store.CanLinkBookingAsync(request.BookingId.Value, userId, ct))
                throw new Fitmate.Domain.Common.NotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
            ticket.LinkBooking(request.BookingId.Value);
        }

        await _store.AddAsync(ticket, ct);
        await _store.SaveChangesAsync(ct);

        return MapToDetailDto(ticket);
    }

    public async Task<SupportTicketDetailDto?> GetTicketAsync(
        Guid ticketId,
        Guid actorUserId,
        bool isAdmin,
        CancellationToken ct)
    {
        var ticket = await _store.FindByIdAsync(ticketId, ct);
        if (ticket is null)
            return null;

        // IDOR Protection: Non-admin users can ONLY see their own tickets.
        // Return null so the controller responds with 404 Not Found to prevent probing.
        if (!isAdmin && ticket.UserId != actorUserId)
            return null;

        return MapToDetailDto(ticket);
    }

    public async Task<PagedSupportTicketsResult> ListMyTicketsAsync(
        Guid userId,
        string? statusStr,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var (clampedPage, clampedPageSize) = ClampPagination(page, pageSize);

        SupportTicketStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusStr) &&
            Enum.TryParse<SupportTicketStatus>(statusStr, true, out var parsedStatus) &&
            Enum.IsDefined(parsedStatus))
        {
            status = parsedStatus;
        }

        var (items, total) = await _store.ListByUserPagedAsync(
            userId, status, clampedPage, clampedPageSize, ct);

        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PagedSupportTicketsResult(dtos, total, clampedPage, clampedPageSize);
    }

    public async Task<SupportTicketDetailDto?> UserReplyTicketAsync(
        Guid ticketId,
        Guid userId,
        ReplySupportTicketRequest request,
        CancellationToken ct)
    {
        var ticket = await _store.FindByIdAsync(ticketId, ct);
        if (ticket is null || ticket.UserId != userId)
            return null;

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reply message is required.",
                new Dictionary<string, string[]> { ["message"] = new[] { "required" } });

        if (ticket.Status == SupportTicketStatus.Closed)
            throw new ConflictException(
                ErrorCodes.Support.TicketClosed,
                "Ticket đã đóng, không thể trả lời thêm.");

        ticket.AddUserReply(userId, request.Message, request.AttachmentUrls);
        await _store.SaveChangesAsync(ct);

        return MapToDetailDto(ticket);
    }

    public async Task<bool> UserCloseTicketAsync(
        Guid ticketId,
        Guid userId,
        CancellationToken ct)
    {
        var ticket = await _store.FindByIdAsync(ticketId, ct);
        if (ticket is null || ticket.UserId != userId)
            return false;

        ticket.CloseByUser(userId);
        await _store.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PagedSupportTicketsResult> ListAdminTicketsAsync(
        string? statusStr,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var (clampedPage, clampedPageSize) = ClampPagination(page, pageSize);

        SupportTicketStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusStr) &&
            Enum.TryParse<SupportTicketStatus>(statusStr, true, out var parsedStatus) &&
            Enum.IsDefined(parsedStatus))
        {
            status = parsedStatus;
        }

        var (items, total) = await _store.ListAdminPagedAsync(
            status, category, clampedPage, clampedPageSize, ct);

        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PagedSupportTicketsResult(dtos, total, clampedPage, clampedPageSize);
    }

    public async Task<SupportTicketDetailDto?> AdminReplyTicketAsync(
        Guid ticketId,
        Guid adminUserId,
        ReplySupportTicketRequest request,
        CancellationToken ct)
    {
        var ticket = await _store.FindByIdAsync(ticketId, ct);
        if (ticket is null)
            return null;

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "Reply message is required.",
                new Dictionary<string, string[]> { ["message"] = new[] { "required" } });

        if (ticket.Status == SupportTicketStatus.Closed)
            throw new ConflictException(
                ErrorCodes.Support.TicketClosed,
                "Ticket đã đóng, không thể trả lời thêm.");

        ticket.AddAdminReply(adminUserId, request.Message, request.AttachmentUrls);
        await _store.SaveChangesAsync(ct);

        _outbox?.Enqueue("support.ticket_replied", new
        {
            TicketId = ticket.Id,
            UserId = ticket.UserId,
            AdminUserId = adminUserId,
            TicketCode = ticket.Id.ToString()[..8].ToUpperInvariant(),
            CreatedAtUtc = DateTime.UtcNow
        });

        return MapToDetailDto(ticket);
    }

    public async Task<SupportTicketDetailDto?> AdminChangeStatusAsync(
        Guid ticketId,
        AdminChangeStatusRequest request,
        CancellationToken ct)
    {
        var ticket = await _store.FindByIdAsync(ticketId, ct);
        if (ticket is null)
            return null;

        if (!Enum.TryParse<SupportTicketStatus>(request.Status, true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                $"Invalid ticket status '{request.Status}'.",
                new Dictionary<string, string[]> { ["status"] = new[] { "not a defined ticket status" } });

        ticket.ChangeStatus(parsedStatus);
        await _store.SaveChangesAsync(ct);

        return MapToDetailDto(ticket);
    }

    private static (int Page, int PageSize) ClampPagination(int page, int pageSize)
    {
        var p = page < 1 ? 1 : page;
        var ps = pageSize < 1 ? 20 : (pageSize > 100 ? 100 : pageSize);
        return (p, ps);
    }

    private static SupportTicketSummaryDto MapToSummaryDto(SupportTicket t) => new(
        t.Id,
        t.UserId,
        t.Subject,
        t.Category,
        t.Priority.ToString(),
        t.Status.ToString(),
        t.CreatedAt,
        t.UpdatedAt,
        t.ResolvedAt,
        t.ClosedAt);

    private static SupportTicketDetailDto MapToDetailDto(SupportTicket t) => new(
        t.Id,
        t.UserId,
        t.Subject,
        t.Category,
        t.Priority.ToString(),
        t.Status.ToString(),
        t.CreatedAt,
        t.UpdatedAt,
        t.ResolvedAt,
        t.ClosedAt,
        t.Messages.Select(m => new SupportTicketMessageDto(
            m.Id,
            m.SenderId,
            m.IsAdmin,
            m.Content,
            m.AttachmentUrls ?? new List<string>(),
            m.CreatedAt)).ToList());
}

