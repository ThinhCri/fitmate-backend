using Fitmate.Domain.Support;

namespace Fitmate.Application.Support;

public interface ISupportTicketStore
{
    Task<SupportTicket?> FindByIdAsync(Guid id, CancellationToken ct);
    Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListByUserPagedAsync(
        Guid userId,
        SupportTicketStatus? status,
        int page,
        int pageSize,
        CancellationToken ct);
    Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListAdminPagedAsync(
        SupportTicketStatus? status,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct);
    Task AddAsync(SupportTicket ticket, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

