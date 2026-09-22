namespace Fitmate.Application.Support;

public interface ISupportService
{
    Task<SupportTicketDetailDto> CreateTicketAsync(
        Guid userId,
        CreateSupportTicketRequest request,
        CancellationToken ct);

    Task<SupportTicketDetailDto?> GetTicketAsync(
        Guid ticketId,
        Guid actorUserId,
        bool isAdmin,
        CancellationToken ct);

    Task<PagedSupportTicketsResult> ListMyTicketsAsync(
        Guid userId,
        string? statusStr,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<SupportTicketDetailDto?> UserReplyTicketAsync(
        Guid ticketId,
        Guid userId,
        ReplySupportTicketRequest request,
        CancellationToken ct);

    Task<bool> UserCloseTicketAsync(
        Guid ticketId,
        Guid userId,
        CancellationToken ct);

    Task<PagedSupportTicketsResult> ListAdminTicketsAsync(
        string? statusStr,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<SupportTicketDetailDto?> AdminReplyTicketAsync(
        Guid ticketId,
        Guid adminUserId,
        ReplySupportTicketRequest request,
        CancellationToken ct);

    Task<SupportTicketDetailDto?> AdminChangeStatusAsync(
        Guid ticketId,
        AdminChangeStatusRequest request,
        CancellationToken ct);
}

