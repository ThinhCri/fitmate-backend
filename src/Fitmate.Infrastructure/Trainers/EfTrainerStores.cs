using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Trainers;

public class EfPtProfileStore : IPtProfileStore
{
    private readonly FitmateDbContext _db;

    public EfPtProfileStore(FitmateDbContext db)
    {
        _db = db;
    }

    public Task<PtProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct)
        => _db.PtProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task AddAsync(PtProfile profile, CancellationToken ct)
        => await _db.PtProfiles.AddAsync(profile, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}

public class EfCertificateStore : ICertificateStore
{
    private readonly FitmateDbContext _db;

    public EfCertificateStore(FitmateDbContext db)
    {
        _db = db;
    }

    public Task<Certificate?> FindByIdentityAsync(Guid ptUserId, string title, string issuedBy, string code, CancellationToken ct)
        => _db.Certificates.FirstOrDefaultAsync(c =>
            c.PtUserId == ptUserId &&
            c.Title == title &&
            c.IssuedBy == issuedBy &&
            c.Code == code &&
            c.Status == CertificateStatus.Pending, ct);

    public Task<Certificate?> FindByIdAsync(Guid certId, CancellationToken ct)
        => _db.Certificates.FirstOrDefaultAsync(c => c.Id == certId, ct);

    public async Task<IReadOnlyList<Certificate>> ListByPtAsync(Guid ptUserId, CancellationToken ct)
        => await _db.Certificates
            .Where(c => c.PtUserId == ptUserId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Certificate> Items, int Total)> ListPendingPagedAsync(int page, int pageSize, CancellationToken ct)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100) pageSize = 100;

        var query = _db.Certificates.Where(c => c.Status == CertificateStatus.Pending);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task AddAsync(Certificate cert, CancellationToken ct)
        => await _db.Certificates.AddAsync(cert, ct);

    public Task SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);
}

public class IdentityUserRoleLookup : IUserRoleLookup
{
    private readonly Microsoft.AspNetCore.Identity.UserManager<Fitmate.Domain.Identity.ApplicationUser> _users;

    public IdentityUserRoleLookup(Microsoft.AspNetCore.Identity.UserManager<Fitmate.Domain.Identity.ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<bool> UserExistsAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user is not null;
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string roleName, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return false;
        return await _users.IsInRoleAsync(user, roleName);
    }

    public async Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return null;
        var roles = await _users.GetRolesAsync(user);
        // Thứ tự ưu tiên: Admin > PT > Member.
        if (roles.Contains("Admin")) return "Admin";
        if (roles.Contains("PT")) return "PT";
        if (roles.Contains("Member")) return "Member";
        return roles.FirstOrDefault();
    }
}
