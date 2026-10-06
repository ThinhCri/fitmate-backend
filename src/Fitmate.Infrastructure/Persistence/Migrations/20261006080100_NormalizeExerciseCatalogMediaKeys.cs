using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261006080100_NormalizeExerciseCatalogMediaKeys")]
public sealed class NormalizeExerciseCatalogMediaKeys : Migration
{
    private static readonly string[] Slugs =
    {
        "Pushups", "Bodyweight_Squat", "Plank", "Bodyweight_Walking_Lunge", "Mountain_Climbers",
        "Single_Leg_Glute_Bridge", "Crunches", "Rope_Jumping", "Dumbbell_Bench_Press",
        "Bent_Over_Two-Dumbbell_Row", "Dumbbell_Shoulder_Press", "Dumbbell_Bicep_Curl",
        "Triceps_Pushdown", "Wide-Grip_Lat_Pulldown", "Barbell_Deadlift", "Barbell_Squat",
        "Push_Up_to_Side_Plank", "Side_Bridge", "Incline_Push-Up", "Chin-Up", "Russian_Twist",
        "Flat_Bench_Lying_Leg_Raise", "Barbell_Hip_Thrust", "Barbell_Seated_Calf_Raise",
        "Lateral_Raise_-_With_Bands", "Scapular_Pull-Up", "Cable_Russian_Twists",
        "Seated_Calf_Raise", "Band_Assisted_Pull-Up", "Hanging_Leg_Raise"
    };

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        for (var index = 0; index < Slugs.Length; index++)
        {
            var id = Guid.Parse($"00000000-0000-4000-8000-{index + 1:D12}");
            var slug = Slugs[index].Replace("'", "''", StringComparison.Ordinal);
            migrationBuilder.Sql($$"""
                UPDATE exercise_catalog
                SET "ImageStartUrl" = 'storage://exercise-catalog/{{slug}}/0.jpg',
                    "ImageEndUrl" = 'storage://exercise-catalog/{{slug}}/1.jpg'
                WHERE id = '{{id}}';
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        for (var index = 0; index < Slugs.Length; index++)
        {
            var id = Guid.Parse($"00000000-0000-4000-8000-{index + 1:D12}");
            var slug = Slugs[index].Replace("'", "''", StringComparison.Ordinal);
            migrationBuilder.Sql($$"""
                UPDATE exercise_catalog
                SET "ImageStartUrl" = 'https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/{{slug}}/0.jpg',
                    "ImageEndUrl" = 'https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/{{slug}}/1.jpg'
                WHERE id = '{{id}}';
                """);
        }
    }
}
