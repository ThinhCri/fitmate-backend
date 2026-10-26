using Fitmate.Application.Notifications;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using Fitmate.Infrastructure.Notifications;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fitmate.IntegrationTests;

public sealed class PushDeliveryConcurrencyTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory factory;
    public PushDeliveryConcurrencyTests(FitmateTestAppFactory factory) { this.factory = factory; factory.EnsureDatabaseInitialized(); }

    [Fact]
    public async Task ConcurrentRegistration_LeavesOnlyOneActiveOwnerForToken()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        using var firstClient = factory.CreateAuthenticatedClient("Member", first);
        using var secondClient = factory.CreateAuthenticatedClient("Member", second);
        var token = new string('a', 40) + Guid.NewGuid().ToString("N");
        using var scopeA = factory.Services.CreateScope(); using var scopeB = factory.Services.CreateScope();
        var results = await Task.WhenAll(
            scopeA.ServiceProvider.GetRequiredService<IDeviceService>().RegisterAsync(first, token, DevicePlatform.Android, PushProvider.Fcm),
            scopeB.ServiceProvider.GetRequiredService<IDeviceService>().RegisterAsync(second, token, DevicePlatform.Android, PushProvider.Fcm));
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var ids = results.Select(x => x.DeviceId).ToArray();
        var rows = await db.Devices.AsNoTracking().Where(d => ids.Contains(d.Id)).ToListAsync();
        rows.Should().HaveCount(2); rows.Count(x => x.IsActive).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentWorkers_ClaimOnce_AndRecoverAbandonedLease()
    {
        var owner = Guid.NewGuid(); using var client = factory.CreateAuthenticatedClient("Member", owner);
        Guid deliveryId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
            var notification = Notification.Create(owner, NotificationType.System, "Test", "Test", null, null, Guid.NewGuid(), "system");
            var device = Device.Create(owner, Guid.NewGuid().ToString("N"), [1, 2, 3], "test-key", DevicePlatform.Android, PushProvider.Fcm);
            var delivery = PushDelivery.Create(notification.Id, device.Id, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddSeconds(-1));
            deliveryId = delivery.Id;
            db.Notifications.Add(notification); db.Devices.Add(device); db.PushDeliveries.Add(delivery); await db.SaveChangesAsync();
        }
        using var a = factory.Services.CreateScope(); using var b = factory.Services.CreateScope();
        var attempts = await Task.WhenAll(
            new EfPushStore(a.ServiceProvider.GetRequiredService<FitmateDbContext>()).TryClaimDeliveryAsync(deliveryId, default),
            new EfPushStore(b.ServiceProvider.GetRequiredService<FitmateDbContext>()).TryClaimDeliveryAsync(deliveryId, default));
        attempts.Count(x => x).Should().Be(1);
        using var check = factory.Services.CreateScope(); var checkDb = check.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var row = await checkDb.PushDeliveries.AsNoTracking().SingleAsync(x => x.Id == deliveryId);
        row.Status.Should().Be(PushDeliveryStatus.Sending); row.Attempts.Should().Be(1);
        await checkDb.PushDeliveries.Where(x => x.Id == deliveryId).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
        (await new EfPushStore(checkDb).TryClaimDeliveryAsync(deliveryId, default)).Should().BeTrue();
    }
}
