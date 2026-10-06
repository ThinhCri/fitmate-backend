using System.Text.Json.Serialization;
using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;

namespace Fitmate.Application.Workout;

public sealed record ExerciseCatalogItemView(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name_vi")] string NameVi,
    [property: JsonPropertyName("name_en")] string NameEn,
    [property: JsonPropertyName("type")]
    [property: JsonConverter(typeof(ExerciseTypeJsonConverter))] ExerciseType Type,
    [property: JsonPropertyName("difficulty")] string Difficulty,
    [property: JsonPropertyName("equipment")] string? Equipment,
    [property: JsonPropertyName("primary_muscles")] IReadOnlyList<string> PrimaryMuscles,
    [property: JsonPropertyName("secondary_muscles")] IReadOnlyList<string> SecondaryMuscles,
    [property: JsonPropertyName("instructions_vi")] IReadOnlyList<string> InstructionsVi,
    [property: JsonPropertyName("image_start_url")] string? ImageStartUrl,
    [property: JsonPropertyName("image_end_url")] string? ImageEndUrl,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("source_url")] string SourceUrl,
    [property: JsonPropertyName("source_license")] string SourceLicense);

public sealed record ExerciseCatalogListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ExerciseCatalogItemView> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total")] int Total);

public interface IExerciseCatalogStore
{
    Task<(IReadOnlyList<ExerciseCatalogEntry> Items, int Total)> SearchAsync(
        string? query, string? muscle, string? equipment, string? difficulty,
        int page, int pageSize, CancellationToken ct);
    Task<ExerciseCatalogEntry?> FindAsync(Guid id, CancellationToken ct);
}

public interface IExerciseCatalogService
{
    Task<ExerciseCatalogListResponse> SearchAsync(
        string? query, string? muscle, string? equipment, string? difficulty,
        int page, int pageSize, CancellationToken ct = default);
    Task<ExerciseCatalogItemView?> FindAsync(Guid id, CancellationToken ct = default);
}
