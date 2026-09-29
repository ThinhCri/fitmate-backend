using Fitmate.Application.Auth;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Auth;

/// <summary>
/// EF Core-backed implementation cho <see cref="IRefreshTokenStore"/>.
/// </summary>
public class EfRefreshTokenStore : IRefreshTokenStore
{
    private readonly FitmateDbContext _db;

    public EfRefreshTokenStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        await _db.RefreshTokens.AddAsync(token, ct);
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
    {
        return _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var sessions = await _db.Set<AuthSession>().Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions) session.RevokedAt = DateTime.UtcNow;
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var t in activeTokens)
        {
            t.Revoke();
        }
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<bool> TrySaveRotationAsync(CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { _db.ChangeTracker.Clear(); return false; }
    }
}
