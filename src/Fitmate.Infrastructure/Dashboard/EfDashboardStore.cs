using Fitmate.Application.Dashboard;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Workout;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Dashboard;

public class EfDashboardStore : IDashboardStore
{
    private readonly FitmateDbContext _db;

    public EfDashboardStore(FitmateDbContext db) => _db = db;

    public async Task<IReadOnlyList<MealLog>> GetMealsForDateAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        return await _db.MealLogs
            .Where(m => m.UserId == userId && m.DeletedAt == null && m.LoggedAt >= startUtc && m.LoggedAt <= endUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WorkoutSession>> GetSessionsForDateAsync(Guid userId, DateOnly date, CancellationToken ct)
    {
        return await _db.WorkoutSessions
            .Where(s => s.UserId == userId && s.DeletedAt == null && s.Date == date)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DateOnly>> GetCompletedSessionDatesAsync(Guid userId, CancellationToken ct)
    {
        return await _db.WorkoutSessions
            .Where(s => s.UserId == userId && s.DeletedAt == null && s.Status == WorkoutSessionStatus.Completed)
            .Select(s => s.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync(ct);
    }

    public async Task<WeightLog?> GetLatestWeightAsync(Guid userId, CancellationToken ct)
    {
        return await _db.WeightLogs
            .Where(w => w.UserId == userId && w.DeletedAt == null)
            .OrderByDescending(w => w.RecordedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Appointment?> GetNextUpcomingAppointmentAsync(Guid userId, DateTime nowUtc, CancellationToken ct)
    {
        return await _db.Appointments
            .Where(a => a.MemberId == userId && a.DeletedAt == null &&
                        (a.Status == BookingStatus.Confirmed || a.Status == BookingStatus.Paid || a.Status == BookingStatus.InProgress) &&
                        a.SlotStartUtc >= nowUtc)
            .OrderBy(a => a.SlotStartUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string?> GetUserNameAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user?.FullName ?? user?.UserName;
    }
}

