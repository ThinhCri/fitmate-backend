using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations;

public partial class FixWorkoutPlanExerciseRelationship : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE exercises
            DROP CONSTRAINT IF EXISTS "FK_exercises_workout_plans_WorkoutPlanId";

            ALTER TABLE exercises
            ADD CONSTRAINT "FK_exercises_workout_plans_WorkoutPlanId"
            FOREIGN KEY ("WorkoutPlanId") REFERENCES workout_plans(id) ON DELETE RESTRICT;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE exercises
            DROP CONSTRAINT IF EXISTS "FK_exercises_workout_plans_WorkoutPlanId";

            ALTER TABLE exercises
            ADD CONSTRAINT "FK_exercises_workout_plans_WorkoutPlanId"
            FOREIGN KEY ("WorkoutPlanId") REFERENCES workout_plans(id) ON DELETE CASCADE;
            """);
    }
}
