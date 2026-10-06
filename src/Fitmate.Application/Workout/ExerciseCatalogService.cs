using Fitmate.Domain.Workout;
using Fitmate.Application.Storage;

namespace Fitmate.Application.Workout;

public sealed class ExerciseCatalogService : IExerciseCatalogService
{
    private readonly IExerciseCatalogStore _store;
    private readonly ISignedUrlService _signedUrls;

    public ExerciseCatalogService(IExerciseCatalogStore store, ISignedUrlService signedUrls)
    {
        _store = store;
        _signedUrls = signedUrls;
    }

    public async Task<ExerciseCatalogListResponse> SearchAsync(
        string? query, string? muscle, string? equipment, string? difficulty,
        int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var (items, total) = await _store.SearchAsync(
            Normalize(query), Normalize(muscle), Normalize(equipment), Normalize(difficulty),
            page, pageSize, ct);
        return new ExerciseCatalogListResponse(items.Select(Map).ToList(), page, pageSize, total);
    }

    public async Task<ExerciseCatalogItemView?> FindAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _store.FindAsync(id, ct);
        return entry is null ? null : Map(entry);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private ExerciseCatalogItemView Map(ExerciseCatalogEntry entry) => new(
        entry.Id, entry.Slug, entry.NameVi, entry.NameEn, entry.Type,
        entry.Difficulty, entry.Equipment, entry.PrimaryMuscles,
        entry.SecondaryMuscles, entry.InstructionsVi, ResolveMedia(entry.ImageStartUrl),
        ResolveMedia(entry.ImageEndUrl), entry.Source, entry.SourceUrl, entry.SourceLicense);

    private string? ResolveMedia(string? value)
    {
        const string prefix = "storage://";
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return value;
        return _signedUrls.CreatePresignedReadUrl(value[prefix.Length..], TimeSpan.FromMinutes(15)).ToString();
    }
}
