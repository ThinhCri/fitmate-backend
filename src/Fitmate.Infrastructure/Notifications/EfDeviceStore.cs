using Fitmate.Application.Notifications;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Notifications;

public class EfDeviceStore : IDeviceStore
{
    private readonly FitmateDbContext _db;

    public EfDeviceStore(FitmateDbContext db) => _db = db;

    public async Task<Device?> FindByTokenHashAsync(
        Guid userId, string tokenHash, CancellationToken ct)
    {
        return await _db.Devices
            .FirstOrDefaultAsync(d =>
                d.UserId == userId &&
                d.TokenHash == tokenHash,
                ct);
    }

    public async Task<Device?> FindByIdForOwnerAsync(
        Guid deviceId, Guid userId, CancellationToken ct)
    {
        return await _db.Devices
            .FirstOrDefaultAsync(d =>
                d.Id == deviceId &&
                d.UserId == userId,
                ct);
    }

    public async Task AddAsync(Device device, CancellationToken ct)
    {
        await _db.Devices.AddAsync(device, ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<Device>> GetActiveDevicesAsync(
        Guid userId, CancellationToken ct)
    {
        return await _db.Devices
            .Where(d => d.UserId == userId && d.IsActive)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Device>> FindActiveDevicesByTokenHashAsync(
        string tokenHash, CancellationToken ct)
    {
        return await _db.Devices
            .Where(d => d.TokenHash == tokenHash && d.IsActive)
            .ToListAsync(ct);
    }
}
