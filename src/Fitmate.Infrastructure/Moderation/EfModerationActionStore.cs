using Fitmate.Application.Moderation;
using Fitmate.Domain.Moderation;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Moderation;

/// <summary>
/// EF Core impl cho <see cref="IModerationActionStore"/>.
/// </summary>
public class EfModerationActionStore : IModerationActionStore
{
    private readonly FitmateDbContext _db;

    public EfModerationActionStore(FitmateDbContext db) { _db = db; }

    public Task AddAsync(UserModerationAction action, CancellationToken ct)
    {
        _db.UserModerationActions.Add(action);
        return Task.CompletedTask; // Caller commits via SaveChangesAsync().
    }
}
