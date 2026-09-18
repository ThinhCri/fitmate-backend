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

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }
}
