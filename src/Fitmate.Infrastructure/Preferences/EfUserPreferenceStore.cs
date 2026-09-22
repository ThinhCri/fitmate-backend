using Fitmate.Application.Preferences;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Preferences;

public class EfUserPreferenceStore : IUserPreferenceStore
{
    private readonly FitmateDbContext _db;

    public EfUserPreferenceStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<UserPreference?> FindByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return await _db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == userId, ct);
    }

    public async Task AddAsync(UserPreference preference, CancellationToken ct)
    {
        await _db.UserPreferences.AddAsync(preference, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}

