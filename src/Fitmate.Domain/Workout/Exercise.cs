using Fitmate.Domain.Common;

namespace Fitmate.Domain.Workout;

public class Exercise : AuditableEntity
{
    public Guid Id { get; private set; }
    public Guid WorkoutPlanId { get; private set; }
    public string Name { get; private set; } = null!;
    public ExerciseType Type { get; private set; }
    public int Sets { get; private set; }
    public int? Reps { get; private set; }
    public int? DurationSec { get; private set; }
    public int RestSec { get; private set; }
    public int? RestAfterSec { get; private set; }
    public int Calories { get; private set; }
    public string? Notes { get; private set; }
    public string? MediaUrl { get; private set; }
    public string? MediaType { get; private set; }
    public string? MediaSource { get; private set; }
    public List<string> Tags { get; private set; } = new();

    private Exercise() { }
    public static Exercise Create(Guid workoutPlanId, string name, ExerciseType type, int sets,
        int? reps, int? durationSec, int restSec, int? restAfterSec, int calories,
        string? notes, string? mediaUrl, string? mediaType, string? mediaSource, List<string> tags) => new()
    {
        Id = Guid.NewGuid(),
        WorkoutPlanId = workoutPlanId,
        Name = name,
        Type = type,
        Sets = sets,
        Reps = reps,
        DurationSec = durationSec,
        RestSec = restSec,
        RestAfterSec = restAfterSec,
        Calories = calories,
        Notes = notes,
        MediaUrl = mediaUrl,
        MediaType = mediaType,
        MediaSource = mediaSource,
        Tags = tags,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
