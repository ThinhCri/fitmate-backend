using Fitmate.Application.Identity;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Identity;

public class EfAccountDeletionStore : IAccountDeletionStore
{
    private readonly FitmateDbContext _db;

    public EfAccountDeletionStore(FitmateDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationUser?> FindUserByIdAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
    }

    public async Task<ApplicationUser?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email, ct);
    }

    public async Task<bool> HasActiveBookingsAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Appointments.AnyAsync(a =>
            (a.MemberId == userId || a.PtUserId == userId) &&
            (a.Status == AppointmentStatus.Held || a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.InProgress),
            ct);
    }

    public async Task<UserProfile?> GetUserProfileAsync(Guid userId, CancellationToken ct)
    {
        return await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
    {
        var role = await (
            from ur in _db.UserRoles
            join r in _db.Roles on ur.RoleId equals r.Id
            where ur.UserId == userId
            select r.Name
        ).FirstOrDefaultAsync(ct);

        return role;
    }

    public async Task RevokeUserDevicesAsync(Guid userId, CancellationToken ct)
    {
        var devices = await _db.Devices
            .Where(d => d.UserId == userId && d.IsActive)
            .ToListAsync(ct);

        foreach (var device in devices)
        {
            device.Revoke();
        }
    }

    public async Task SaveChangesAsync(CancellationToken ct)
    {
        await _db.SaveChangesAsync(ct);
    }
}

