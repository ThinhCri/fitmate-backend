using Fitmate.Application.Identity;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public Task<T> WithUserLockAsync<T>(Guid userId, Func<Task<T>> action, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM asp_net_users WHERE \"Id\"=@userId FOR UPDATE",
                [new NpgsqlParameter("userId", userId)], ct);
            var result = await action();
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public Task<ApplicationUser?> FindIncludingPendingAsync(Guid userId, CancellationToken ct) =>
        _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task<(IReadOnlyList<ApplicationUser> Items, int Total)> ListForAdminAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.DeletionRequestedAt != null || u.Status == UserStatus.PendingDeletion)
            .OrderBy(u => u.Status == UserStatus.PendingDeletion ? u.DeletedAt : u.DeletionRequestedAt)
            .ThenBy(u => u.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<ApplicationUser>> ListDueForFinalizationAsync(DateTime cutoffUtc, int limit, CancellationToken ct) =>
        await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Status == UserStatus.PendingDeletion && u.DeletedAt <= cutoffUtc)
            .OrderBy(u => u.DeletedAt).ThenBy(u => u.Id).Take(limit).ToListAsync(ct);

    public async Task<ApplicationUser?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        return await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email, ct);
    }

    public async Task<bool> HasActiveBookingsAsync(Guid userId, CancellationToken ct)
    {
        if (await _db.Appointments.AnyAsync(a =>
            (a.MemberId == userId || a.PtUserId == userId) &&
            (a.Status == AppointmentStatus.Held || a.Status == AppointmentStatus.Pending ||
             a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.InProgress ||
             a.Status == AppointmentStatus.AwaitingMemberConfirmation || a.Status == AppointmentStatus.Disputed ||
             a.Status == AppointmentStatus.NoShow), ct)) return true;

        if (await _db.SessionPackages.AnyAsync(p =>
            (p.MemberId == userId || p.PtUserId == userId) &&
            (p.Status == SessionPackageStatus.Held || p.Status == SessionPackageStatus.PaymentPending ||
             p.Status == SessionPackageStatus.Active || p.Status == SessionPackageStatus.StopRequested ||
             p.Status == SessionPackageStatus.LatePaymentRefundPending || p.Status == SessionPackageStatus.OnlinePreparing), ct))
            return true;

        if (await _db.Payments.AnyAsync(payment => payment.Status == PaymentStatus.Pending &&
            ((payment.AppointmentId != null && _db.Appointments.Any(a => a.Id == payment.AppointmentId &&
                (a.MemberId == userId || a.PtUserId == userId))) ||
             (payment.PackageId != null && _db.SessionPackages.Any(p => p.Id == payment.PackageId &&
                (p.MemberId == userId || p.PtUserId == userId)))), ct)) return true;

        if (await _db.Refunds.AnyAsync(refund => refund.Status == RefundStatus.Pending &&
            _db.Appointments.Any(a => a.Id == refund.AppointmentId &&
                (a.MemberId == userId || a.PtUserId == userId)), ct)) return true;

        if (await _db.AiSubscriptions.AnyAsync(subscription => subscription.UserId == userId &&
            subscription.State == "SUBSCRIPTION_STATE_ACTIVE" &&
            (subscription.ExpiresAtUtc == null || subscription.ExpiresAtUtc > DateTime.UtcNow), ct)) return true;

        return await _db.Set<Fitmate.Domain.Payment.ManualPtSettlement>().AnyAsync(settlement =>
            settlement.PtUserId == userId && settlement.Status != "paid" && settlement.AmountVnd > 0, ct);
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

