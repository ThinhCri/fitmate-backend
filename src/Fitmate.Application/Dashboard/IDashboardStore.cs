using Fitmate.Domain.Booking;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Workout;
using Fitmate.Domain.Identity;

namespace Fitmate.Application.Dashboard;

public interface IDashboardStore
{
    Task<IReadOnlyList<MealLog>> GetMealsForDateAsync(Guid userId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<WorkoutSession>> GetSessionsForDateAsync(Guid userId, DateOnly date, CancellationToken ct);
    Task<IReadOnlyList<DateOnly>> GetCompletedSessionDatesAsync(Guid userId, CancellationToken ct);
    Task<WeightLog?> GetLatestWeightAsync(Guid userId, CancellationToken ct);
    Task<Appointment?> GetNextUpcomingAppointmentAsync(Guid userId, DateTime nowUtc, CancellationToken ct);
    Task<string?> GetUserNameAsync(Guid userId, CancellationToken ct);
    Task<UserPreference?> GetUserPreferenceAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<UserPreference?>(null);
    Task<WorkoutPlan?> GetWorkoutPlanForDayAsync(Guid userId, string dayOfWeek, CancellationToken ct) =>
        Task.FromResult<WorkoutPlan?>(null);
}

