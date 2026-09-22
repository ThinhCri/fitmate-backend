using Fitmate.Application.Notifications;
using Fitmate.Domain.Common;
using Fitmate.Domain.Notifications;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-200: DeviceService.Register/Revoke.
/// Idempotency: same (userId, tokenHash) re-register refreshes (no duplicate row).
/// Authorization: cross-user revoke returns 404.
/// Validation: token length/charset enforced.
/// </summary>
public class DeviceServiceTests
{
    private sealed class InMemoryDeviceStore : IDeviceStore
    {
        public Dictionary<Guid, Device> ById { get; } = new();

        public Task<Device?> FindByTokenHashAsync(Guid userId, string tokenHash, CancellationToken ct)
            => Task.FromResult(ById.Values.FirstOrDefault(d => d.UserId == userId && d.TokenHash == tokenHash));

        public Task<Device?> FindByIdForOwnerAsync(Guid deviceId, Guid userId, CancellationToken ct)
            => Task.FromResult(ById.TryGetValue(deviceId, out var d) && d.UserId == userId ? d : null);

        public Task AddAsync(Device d, CancellationToken ct)
        {
            ById[d.Id] = d;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<Device>> GetActiveDevicesAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(ById.Values.Where(d => d.UserId == userId && d.IsActive).ToList());

        public Task<IReadOnlyList<Device>> FindActiveDevicesByTokenHashAsync(string tokenHash, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<Device>>(ById.Values.Where(d => d.TokenHash == tokenHash && d.IsActive).ToList());
    }

    private sealed class StubCipher : IPushDeliveryTokens
    {
        public byte[] Protect(string rawToken) => System.Text.Encoding.UTF8.GetBytes($"enc:{rawToken}");
        public string Unprotect(byte[] encryptedToken) => System.Text.Encoding.UTF8.GetString(encryptedToken)["enc:".Length..];
    }

    private static DeviceService NewService(InMemoryDeviceStore store) => new(store, new StubCipher());

    private static string ValidToken(string suffix = "x") =>
        // length 36 chars (> MinTokenLength 32) + charset
        $"ABCDEFGHIJKLMNOPQRSTUVWXYZ012345{suffix}";

    [Fact]
    public async Task Register_NewDevice_Persists()
    {
        var store = new InMemoryDeviceStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();

        var result = await svc.RegisterAsync(userId, ValidToken(), DevicePlatform.Android, PushProvider.Fcm);

        result.DeviceId.Should().NotBe(Guid.Empty);
        store.ById.Should().HaveCount(1);
        var d = store.ById.Values.First();
        d.UserId.Should().Be(userId);
        d.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Register_DuplicateSameUser_IsIdempotent()
    {
        var store = new InMemoryDeviceStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();
        var token = ValidToken();

        var first = await svc.RegisterAsync(userId, token, DevicePlatform.Android, PushProvider.Fcm);
        var second = await svc.RegisterAsync(userId, token, DevicePlatform.Android, PushProvider.Fcm);

        store.ById.Should().HaveCount(1); // still 1 row, refreshed
        first.DeviceId.Should().Be(second.DeviceId);
    }

    [Fact]
    public async Task Register_SameTokenDifferentUsers_RevokesPreviousUserActiveDevice()
    {
        var store = new InMemoryDeviceStore();
        var svc = NewService(store);
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var token = ValidToken();

        var resA = await svc.RegisterAsync(userA, token, DevicePlatform.Android, PushProvider.Fcm);
        store.ById[resA.DeviceId].IsActive.Should().BeTrue();

        var resB = await svc.RegisterAsync(userB, token, DevicePlatform.Android, PushProvider.Fcm);

        store.ById.Should().HaveCount(2);
        store.ById[resA.DeviceId].IsActive.Should().BeFalse("previous owner's device must be revoked to prevent push leak");
        store.ById[resA.DeviceId].RevokedAt.Should().NotBeNull();
        store.ById[resB.DeviceId].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Register_PreviouslyRevokedDeviceForSameUser_ReactivatesDevice()
    {
        var store = new InMemoryDeviceStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();
        var token = ValidToken();

        var first = await svc.RegisterAsync(userId, token, DevicePlatform.Android, PushProvider.Fcm);
        await svc.RevokeAsync(userId, first.DeviceId);
        store.ById[first.DeviceId].IsActive.Should().BeFalse();

        var second = await svc.RegisterAsync(userId, token, DevicePlatform.Android, PushProvider.Fcm);

        second.DeviceId.Should().Be(first.DeviceId);
        store.ById[first.DeviceId].IsActive.Should().BeTrue("device must be reactivated");
        store.ById[first.DeviceId].RevokedAt.Should().BeNull("revoked timestamp must be cleared on reactivation");
    }

    [Fact]
    public async Task Register_EmptyUser_ThrowsUnauthorized()
    {
        var svc = NewService(new InMemoryDeviceStore());
        var act = () => svc.RegisterAsync(Guid.Empty, ValidToken(), DevicePlatform.Android, PushProvider.Fcm);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Register_TooShortToken_ThrowsValidation()
    {
        var svc = NewService(new InMemoryDeviceStore());
        var act = () => svc.RegisterAsync(Guid.NewGuid(), "short", DevicePlatform.Android, PushProvider.Fcm);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Register_InvalidCharset_ThrowsValidation()
    {
        var svc = NewService(new InMemoryDeviceStore());
        // 32 chars but contains '!' which is not in charset
        var bad = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123!";
        var act = () => svc.RegisterAsync(Guid.NewGuid(), bad, DevicePlatform.Android, PushProvider.Fcm);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Revoke_OwnedDevice_SetsInactive()
    {
        var store = new InMemoryDeviceStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();
        var result = await svc.RegisterAsync(userId, ValidToken(), DevicePlatform.Android, PushProvider.Fcm);

        await svc.RevokeAsync(userId, result.DeviceId);

        store.ById[result.DeviceId].IsActive.Should().BeFalse();
        store.ById[result.DeviceId].RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_OtherUsersDevice_Throws404()
    {
        var store = new InMemoryDeviceStore();
        var svc = NewService(store);
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();
        var result = await svc.RegisterAsync(owner, ValidToken(), DevicePlatform.Android, PushProvider.Fcm);

        var act = () => svc.RevokeAsync(attacker, result.DeviceId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Revoke_UnknownId_Throws404()
    {
        var svc = NewService(new InMemoryDeviceStore());
        var act = () => svc.RevokeAsync(Guid.NewGuid(), Guid.NewGuid());
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Revoke_EmptyDeviceId_ThrowsValidation()
    {
        var svc = NewService(new InMemoryDeviceStore());
        var act = () => svc.RevokeAsync(Guid.NewGuid(), Guid.Empty);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Revoke_EmptyUser_ThrowsUnauthorized()
    {
        var svc = NewService(new InMemoryDeviceStore());
        var act = () => svc.RevokeAsync(Guid.Empty, Guid.NewGuid());
        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
