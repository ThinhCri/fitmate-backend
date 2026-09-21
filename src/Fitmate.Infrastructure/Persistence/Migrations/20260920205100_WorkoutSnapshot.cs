using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// BE-160: extend workout_sessions with plan snapshot + add
    /// workout_session_exercises (planned snapshot of each exercise in a session)
    /// and workout_session_exercise_sets (per-set results). Also wires up FK
    /// constraints on exercises/workout_sessions that were missing from the
    /// initial schema. See <c>docs/backend-release/tasks/BE-160-plan.md</c>.
    /// </summary>
    public partial class WorkoutSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BE-160 pre-check: abort if orphan exercises or orphan sessions exist,
            // because the new FK constraints would otherwise fail. Run before any DDL
            // so a noisy dev DB doesn't break a CI apply.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    orphan_exercises INT;
    orphan_sessions INT;
BEGIN
    SELECT COUNT(*) INTO orphan_exercises
    FROM exercises e
    WHERE NOT EXISTS (SELECT 1 FROM workout_plans p WHERE p.id = e.""WorkoutPlanId"");

    SELECT COUNT(*) INTO orphan_sessions
    FROM workout_sessions s
    WHERE s.""WorkoutPlanId"" IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM workout_plans p WHERE p.id = s.""WorkoutPlanId"");

    RAISE NOTICE 'WorkoutSnapshot pre-check: orphan_exercises=%, orphan_sessions=%', orphan_exercises, orphan_sessions;

    IF orphan_exercises > 0 OR orphan_sessions > 0 THEN
        RAISE EXCEPTION 'WorkoutSnapshot aborted: orphan_exercises=%, orphan_sessions=%. Resolve orphans before applying.', orphan_exercises, orphan_sessions;
    END IF;
END $$;");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotPlanDayOfWeek",
                table: "workout_sessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotPlanName",
                table: "workout_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotPlanPlannedDurationMinutes",
                table: "workout_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotPlanTargetCaloriesBurn",
                table: "workout_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workout_session_exercises",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExerciseName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    PlannedSets = table.Column<int>(type: "integer", nullable: false),
                    PlannedReps = table.Column<int>(type: "integer", nullable: true),
                    PlannedDurationSec = table.Column<int>(type: "integer", nullable: true),
                    PlannedRestSec = table.Column<int>(type: "integer", nullable: true),
                    PlannedCalories = table.Column<int>(type: "integer", nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    CompletedSets = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workout_session_exercises", x => x.id);
                    table.ForeignKey(
                        name: "FK_workout_session_exercises_workout_sessions_WorkoutSessionId",
                        column: x => x.WorkoutSessionId,
                        principalTable: "workout_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workout_session_exercise_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkoutSessionExerciseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SetIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActualReps = table.Column<int>(type: "integer", nullable: true),
                    ActualDurationSec = table.Column<int>(type: "integer", nullable: true),
                    IsBonus = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workout_session_exercise_sets", x => x.id);
                    table.ForeignKey(
                        name: "FK_workout_session_exercise_sets_workout_session_exercises_Wor~",
                        column: x => x.WorkoutSessionExerciseId,
                        principalTable: "workout_session_exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workout_session_exercise_sets_WorkoutSessionExerciseId_SetI~",
                table: "workout_session_exercise_sets",
                columns: new[] { "WorkoutSessionExerciseId", "SetIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workout_session_exercises_WorkoutSessionId",
                table: "workout_session_exercises",
                column: "WorkoutSessionId");

            // BE-160: wire FK constraints that were missing from InitialSchema.
            // exercises -> workout_plans: CASCADE (a plan owns its exercises).
            migrationBuilder.Sql(@"
ALTER TABLE exercises
    ADD CONSTRAINT ""FK_exercises_workout_plans_WorkoutPlanId""
    FOREIGN KEY (""WorkoutPlanId"") REFERENCES workout_plans(id) ON DELETE CASCADE;");

            // workout_sessions -> workout_plans: SET NULL (deleting a plan keeps
            // historic sessions; their content lives in snapshot columns +
            // workout_session_exercises).
            migrationBuilder.Sql(@"
ALTER TABLE workout_sessions
    ADD CONSTRAINT ""FK_workout_sessions_workout_plans_WorkoutPlanId""
    FOREIGN KEY (""WorkoutPlanId"") REFERENCES workout_plans(id) ON DELETE SET NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE workout_sessions DROP CONSTRAINT IF EXISTS ""FK_workout_sessions_workout_plans_WorkoutPlanId"";
ALTER TABLE exercises DROP CONSTRAINT IF EXISTS ""FK_exercises_workout_plans_WorkoutPlanId"";");

            migrationBuilder.DropTable(
                name: "workout_session_exercise_sets");

            migrationBuilder.DropTable(
                name: "workout_session_exercises");

            migrationBuilder.DropColumn(
                name: "SnapshotPlanDayOfWeek",
                table: "workout_sessions");

            migrationBuilder.DropColumn(
                name: "SnapshotPlanName",
                table: "workout_sessions");

            migrationBuilder.DropColumn(
                name: "SnapshotPlanPlannedDurationMinutes",
                table: "workout_sessions");

            migrationBuilder.DropColumn(
                name: "SnapshotPlanTargetCaloriesBurn",
                table: "workout_sessions");
        }
    }
}
