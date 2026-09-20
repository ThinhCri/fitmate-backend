using Fitmate.Application.Notifications;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fitmate.Infrastructure.Notifications;

/// <summary>
/// Infrastructure implementation of <see cref="IPushDeliveryDispatcher"/>.
/// Same logic as the Application-layer dispatcher but with DbContext access for
/// the save transaction.
/// </summary>
public class EfPushDeliveryDispatcher : PushDeliveryDispatcher
{
    private readonly FitmateDbContext _db;

    public EfPushDeliveryDispatcher(
        FitmateDbContext db,
        INotificationStore notifications,
        IDeviceStore devices,
        IPushStore pushes,
        IOutboxDispatcher outbox,
        INotificationAuditLogger audit,
        ILogger<EfPushDeliveryDispatcher> logger)
        : base(notifications, devices, pushes, outbox, audit)
    {
        _db = db;
    }

    public async Task<Guid> DispatchAndSaveAsync(
        Application.Notifications.NotificationPayload payload,
        CancellationToken ct)
    {
        var id = await DispatchAsync(payload, ct);
        await _db.SaveChangesAsync(ct);
        return id;
    }
}
