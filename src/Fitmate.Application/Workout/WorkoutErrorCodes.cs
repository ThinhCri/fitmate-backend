using Fitmate.Domain.Common;

namespace Fitmate.Application.Workout;

public static class WorkoutErrorCodes
{
    public const string SessionNotFound = "FITMATE.WORKOUT.SESSION_NOT_FOUND";
    public const string DuplicateClientId = "FITMATE.WORKOUT.DUPLICATE_CLIENT_ID";
    public const string IdempotencyMismatch = "FITMATE.WORKOUT.IDEMPOTENCY_MISMATCH";
    public const string PlanNotFound = "FITMATE.WORKOUT.PLAN_NOT_FOUND";
    public const string ExerciseNotFound = "FITMATE.WORKOUT.EXERCISE_NOT_FOUND";
    public const string WeightNotFound = "FITMATE.WORKOUT.WEIGHT_NOT_FOUND";
}
