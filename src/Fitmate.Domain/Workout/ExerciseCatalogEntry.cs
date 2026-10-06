using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

/// <summary>
/// Curated, licensed exercise reference. Plans copy display/tracking fields so
/// historical sessions remain stable when the catalog is updated.
/// </summary>
public class ExerciseCatalogEntry : AuditableEntity
{
    public Guid Id { get; private set; }
    public string Slug { get; private set; } = null!;
    public string NameVi { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public ExerciseType Type { get; private set; }
    public string Difficulty { get; private set; } = null!;
    public string? Equipment { get; private set; }
    public List<string> PrimaryMuscles { get; private set; } = new();
    public List<string> SecondaryMuscles { get; private set; } = new();
    public List<string> InstructionsVi { get; private set; } = new();
    public string? ImageStartUrl { get; private set; }
    public string? ImageEndUrl { get; private set; }
    public string Source { get; private set; } = null!;
    public string SourceUrl { get; private set; } = null!;
    public string SourceLicense { get; private set; } = null!;
    public bool IsActive { get; private set; }

    private ExerciseCatalogEntry() { }
}
