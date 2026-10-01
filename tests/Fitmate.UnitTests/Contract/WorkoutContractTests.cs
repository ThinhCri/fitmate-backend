using System.Text.Json;
using Fitmate.Application.Workout;
using Fitmate.Domain.Common;
using FluentAssertions;
using Microsoft.OpenApi.Readers;
using Xunit;

namespace Fitmate.UnitTests.Contract;

public class WorkoutContractTests
{
    private static readonly string OpenApiPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../docs/backend-release/openapi/fitmate-v1.yaml"));

    [Fact]
    public void WorkoutPlanView_Serializes_WithExactExpectedKeys_AndNoCamelCase()
    {
        var plan = new WorkoutPlanView(
            Id: Guid.NewGuid(),
            DayOfWeek: "mon",
            WorkoutName: "Upper Body",
            DurationMinutes: 45,
            TargetCaloriesBurn: 300,
            IsCompleted: false,
            ExerciseCount: 4,
            CreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(plan);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "day_of_week",
            "workout_name",
            "duration_minutes",
            "target_calories_burn",
            "is_completed",
            "exercise_count",
            "created_at"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        root.GetProperty("workout_name").GetString().Should().Be("Upper Body");
    }

    [Fact]
    public void WorkoutPlanDetailView_Serializes_WithExactExpectedKeys()
    {
        var detail = new WorkoutPlanDetailView(
            Id: Guid.NewGuid(),
            DayOfWeek: "tue",
            WorkoutName: "Lower Body",
            DurationMinutes: 50,
            TargetCaloriesBurn: 400,
            IsCompleted: true,
            CreatedAt: DateTime.UtcNow,
            Exercises: new List<ExerciseItemView>
            {
                new(
                    Id: Guid.NewGuid(),
                    WorkoutPlanId: Guid.NewGuid(),
                    Name: "Squat",
                    Type: ExerciseType.Reps,
                    Sets: 4,
                    Reps: 12,
                    DurationSec: null,
                    RestSec: 60,
                    RestAfterSec: 90,
                    Calories: 100,
                    Notes: "Deep squat",
                    MediaUrl: null,
                    MediaType: null,
                    MediaSource: null,
                    Tags: new List<string> { "legs", "compound" }
                )
            }
        );

        var json = JsonSerializer.Serialize(detail);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "day_of_week",
            "workout_name",
            "duration_minutes",
            "target_calories_burn",
            "is_completed",
            "created_at",
            "exercises"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        var ex = root.GetProperty("exercises")[0];
        ex.GetProperty("type").GetString().Should().Be("reps");
        ex.GetProperty("workout_plan_id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void WorkoutSessionView_Serializes_WithExactExpectedKeys_AndSnakeCaseEnum()
    {
        var session = new WorkoutSessionView(
            Id: Guid.NewGuid(),
            PlanId: Guid.NewGuid(),
            PlanName: "Full Body",
            Date: new DateOnly(2026, 10, 2),
            Day: "Fri",
            DurationMin: 60,
            CaloriesBurned: 450,
            Exercises: 5,
            Status: WorkoutSessionStatus.EndedEarly,
            CompletedSets: 10,
            PlannedSets: 15,
            CreatedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(session);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "plan_id",
            "plan_name",
            "date",
            "day",
            "duration_min",
            "calories_burned",
            "exercises",
            "status",
            "completed_sets",
            "planned_sets",
            "created_at"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        root.GetProperty("status").GetString().Should().Be("ended_early");
    }

    [Fact]
    public void WorkoutSetView_Serializes_WithExactExpectedKeys_AndSnakeCaseEnum()
    {
        var set = new WorkoutSetView(
            Id: Guid.NewGuid(),
            SetIndex: 1,
            Status: WorkoutSetStatus.Partial,
            ActualReps: 8,
            ActualDurationSec: null,
            IsBonus: false
        );

        var json = JsonSerializer.Serialize(set);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string>
        {
            "id",
            "set_index",
            "status",
            "actual_reps",
            "actual_duration_sec",
            "is_bonus"
        };

        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
        root.GetProperty("status").GetString().Should().Be("partial");
    }

    [Fact]
    public void WeightLogView_Serializes_WithExactExpectedKeys()
    {
        var weight = new WeightLogView(
            Id: Guid.NewGuid(),
            WeightKg: 72.5m,
            RecordedAt: DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(weight);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var expectedKeys = new HashSet<string> { "id", "weight_kg", "recorded_at" };
        root.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(expectedKeys);
    }

    [Fact]
    public void LogWorkoutSessionRequest_Deserializes_FromSnakeCase()
    {
        var json = """
        {
            "client_id": "client-uuid-123",
            "plan_id": "11111111-1111-1111-1111-111111111111",
            "date": "2026-10-02",
            "duration_min": 45,
            "calories_burned": 320,
            "exercises": 3,
            "status": "completed",
            "completed_sets": 9,
            "planned_sets": 9,
            "exercise_items": [
                {
                    "exercise_id": "22222222-2222-2222-2222-222222222222",
                    "order_index": 0,
                    "planned_reps": 10,
                    "planned_duration_sec": null,
                    "planned_rest_sec": 60,
                    "sets": [
                        {
                            "set_index": 1,
                            "status": "completed",
                            "actual_reps": 10,
                            "actual_duration_sec": null
                        }
                    ]
                }
            ]
        }
        """;

        var req = JsonSerializer.Deserialize<LogWorkoutSessionRequest>(json);
        req.Should().NotBeNull();
        req!.ClientId.Should().Be("client-uuid-123");
        req.Status.Should().Be(WorkoutSessionStatus.Completed);
        req.ExerciseItems.Should().HaveCount(1);
        req.ExerciseItems![0].Sets.Should().HaveCount(1);
        req.ExerciseItems[0].Sets![0].Status.Should().Be(WorkoutSetStatus.Completed);
    }

    [Fact]
    public void ListResponses_Serialize_WithItemsProperty()
    {
        var plan = new WorkoutPlanView(Guid.NewGuid(), "mon", "Cardio", 30, 200, false, 2, DateTime.UtcNow);
        var plansResp = new WorkoutPlanListResponse(new[] { plan });
        var json = JsonSerializer.Serialize(plansResp);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "items" });
    }

    [Fact]
    public void OpenApi_WorkoutSchemas_MatchExpectedProperties()
    {
        File.Exists(OpenApiPath).Should().BeTrue();
        using var stream = File.OpenRead(OpenApiPath);
        var reader = new OpenApiStreamReader();
        var doc = reader.Read(stream, out var diagnostic);
        diagnostic.Errors.Should().BeEmpty();

        doc.Components.Schemas.Should().ContainKey("WorkoutPlan");
        var planSchema = doc.Components.Schemas["WorkoutPlan"];
        var expectedPlanProperties = new HashSet<string>
        {
            "id",
            "day_of_week",
            "workout_name",
            "duration_minutes",
            "target_calories_burn",
            "is_completed",
            "exercise_count",
            "created_at"
        };
        planSchema.Properties.Keys.Should().BeEquivalentTo(expectedPlanProperties);

        doc.Components.Schemas.Should().ContainKey("WorkoutPlanDetail");
        var detailSchema = doc.Components.Schemas["WorkoutPlanDetail"];
        var expectedDetailProperties = new HashSet<string>
        {
            "id",
            "day_of_week",
            "workout_name",
            "duration_minutes",
            "target_calories_burn",
            "is_completed",
            "created_at",
            "exercises"
        };
        detailSchema.Properties.Keys.Should().BeEquivalentTo(expectedDetailProperties);
    }
}
