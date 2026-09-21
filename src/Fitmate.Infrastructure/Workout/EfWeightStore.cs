using Fitmate.Application.Workout;
using Fitmate.Domain.Workout;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Workout;

public class EfWeightStore : IWeightStore
{
    private readonly FitmateDbContext _db;

    public EfWeightStore(FitmateDbContext db) => _db = db;

    public async Task<IReadOnlyList<WeightLog>> ListAsync(Guid userId, CancellationToken ct) =>
        await _db.WeightLogs
            .Where(w => w.UserId == userId && w.DeletedAt == null)
            .OrderByDescending(w => w.RecordedAt)
            .ToListAsync(ct);

    public Task<WeightLog?> FindByIdAsync(Guid weightLogId, CancellationToken ct) =>
        _db.WeightLogs.FirstOrDefaultAsync(w => w.Id == weightLogId && w.DeletedAt == null, ct);

    public async Task AddAsync(WeightLog log, CancellationToken ct) =>
        await _db.WeightLogs.AddAsync(log, ct);

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
