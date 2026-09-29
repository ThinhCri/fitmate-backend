using Fitmate.Application.Support;
using Fitmate.Domain.Support;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Support;

public class EfSupportTicketStore : ISupportTicketStore
{
    public async Task<bool> CanLinkBookingAsync(Guid bookingId, Guid actorId, CancellationToken ct)
    {
        if (await _db.Appointments.AnyAsync(a => a.Id == bookingId && (a.MemberId == actorId || a.PtUserId == actorId), ct)) return true;
        return await (from ur in _db.UserRoles join role in _db.Roles on ur.RoleId equals role.Id
                      where ur.UserId == actorId && role.NormalizedName == "ADMIN" select ur).AnyAsync(ct)
            && await _db.Appointments.AnyAsync(a => a.Id == bookingId, ct);
    }
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
        var ids = _db.ChangeTracker.Entries<SupportTicket>().Where(e => e.Entity.BookingId.HasValue)
            .Select(e => e.Entity.BookingId!.Value).Distinct().OrderBy(x => x).ToList();
        if (!_db.Database.IsNpgsql() || ids.Count == 0) { await _db.SaveChangesAsync(ct); return; }
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        foreach (var id in ids)
            await _db.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {id} FOR UPDATE").LoadAsync(ct);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

