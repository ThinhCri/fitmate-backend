using Fitmate.Application.Workout;
using Fitmate.Domain.Workout;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Workout;

public sealed class EfExerciseCatalogStore : IExerciseCatalogStore
{
    private readonly FitmateDbContext _db;

    public EfExerciseCatalogStore(FitmateDbContext db) => _db = db;

    public async Task<(IReadOnlyList<ExerciseCatalogEntry> Items, int Total)> SearchAsync(
        string? query, string? muscle, string? equipment, string? difficulty,
        int page, int pageSize, CancellationToken ct)
    {
        var items = _db.ExerciseCatalog.AsNoTracking()
            .Where(entry => entry.IsActive && entry.DeletedAt == null);

        if (query is not null)
        {
            var pattern = $"%{query}%";
            items = items.Where(entry =>
                EF.Functions.ILike(entry.NameVi, pattern) ||
                EF.Functions.ILike(entry.NameEn, pattern));
        }
        if (muscle is not null)
            items = items.Where(entry => entry.PrimaryMuscles.Contains(muscle));
        if (equipment is not null)
            items = items.Where(entry => entry.Equipment == equipment);
        if (difficulty is not null)
            items = items.Where(entry => entry.Difficulty == difficulty);

        var total = await items.CountAsync(ct);
        var pageItems = await items.OrderBy(entry => entry.NameVi)
            .ThenBy(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (pageItems, total);
    }

    public Task<ExerciseCatalogEntry?> FindAsync(Guid id, CancellationToken ct) =>
        _db.ExerciseCatalog.AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == id && entry.IsActive && entry.DeletedAt == null, ct);
}
