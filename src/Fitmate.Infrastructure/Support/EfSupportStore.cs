using Fitmate.Application.Support;
using Fitmate.Domain.Support;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Support;

public class EfSupportTicketStore : ISupportTicketStore
{
    private readonly FitmateDbContext _db;

    public EfSupportTicketStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<SupportTicket?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        return await _db.SupportTickets
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListByUserPagedAsync(
        Guid userId,
        SupportTicketStatus? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.SupportTickets
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(IReadOnlyList<SupportTicket> Items, int Total)> ListAdminPagedAsync(
        SupportTicketStatus? status,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var query = _db.SupportTickets
            .AsNoTracking()
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCat = category.Trim().ToLowerInvariant();
            query = query.Where(x => x.Category == normalizedCat);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task AddAsync(SupportTicket ticket, CancellationToken ct)
    {
        await _db.SupportTickets.AddAsync(ticket, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}

