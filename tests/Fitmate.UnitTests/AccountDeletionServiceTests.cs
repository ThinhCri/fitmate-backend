using Fitmate.Application.Auth;
using Fitmate.Application.Identity;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Domain.Notifications;
using Fitmate.Domain.Outbox;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class AccountDeletionServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    [InlineData("１２３４５６")]
    public async Task RequestDeletion_RejectsMissingOrMalformedOtp(string? otp)
    {
        var outbox = new InMemoryOutboxDispatcher();
        var svc = new AccountDeletionService(new InMemoryAccountDeletionStore(), new InMemoryRefreshTokenStore(),
            outbox, new InMemoryDeletionTokenProtector(), new VerifiedDeletionOtpGateway());
        var act = () => svc.RequestDeletionAsync(Guid.NewGuid(), new(null, null, otp), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("DELETION_OTP_REQUIRED");
        outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestDeletion_CannotBypassRejectedOtp()
    {
        var outbox = new InMemoryOutboxDispatcher();
        var svc = new AccountDeletionService(new InMemoryAccountDeletionStore(), new InMemoryRefreshTokenStore(),
            outbox, new InMemoryDeletionTokenProtector(), new VerifiedDeletionOtpGateway());
        var act = () => svc.RequestDeletionAsync(Guid.NewGuid(), new(null, null, "999999"), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("DELETION_OTP_INVALID");
        outbox.Messages.Should().BeEmpty();
    }
    [Fact]
    public async Task RequestDeletionAsync_QueuesForAdminWithoutStartingCountdown()
    {
        var store = new InMemoryAccountDeletionStore();
        var tokenStore = new InMemoryRefreshTokenStore();
        var outbox = new InMemoryOutboxDispatcher();
        var protector = new InMemoryDeletionTokenProtector();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            FullName = "Nguyen Van A",
            Status = UserStatus.Active
        };
        store.Users[user.Id] = user;

        var svc = new AccountDeletionService(store, tokenStore, outbox, protector, new VerifiedDeletionOtpGateway());
        var res = await svc.RequestDeletionAsync(user.Id, new AccountDeletionRequest("Privacy concerns", null, "123456"), CancellationToken.None);

        res.IsPendingDeletion.Should().BeTrue();
        res.Status.Should().Be("active");
        res.RetentionDaysRemaining.Should().Be(0);
        user.Status.Should().Be(UserStatus.Active);
        user.DeletionRequestedAt.Should().NotBeNull();
        user.DeletedAt.Should().BeNull();

        tokenStore.RevokedUserTokens.Should().NotContain(user.Id);
        store.RevokedDeviceUsers.Should().NotContain(user.Id);
        outbox.EnqueuedTypes.Should().Contain("user.deletion_requested");
    }

    [Fact]
    public async Task RequestDeletionAsync_ThrowsConflict_WhenUserHasActiveBookings()
    {
        var store = new InMemoryAccountDeletionStore();
        var tokenStore = new InMemoryRefreshTokenStore();
        var outbox = new InMemoryOutboxDispatcher();
        var protector = new InMemoryDeletionTokenProtector();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "busy@example.com",
            FullName = "Busy User",
            Status = UserStatus.Active
        };
        store.Users[user.Id] = user;
        store.UserHasActiveBookings[user.Id] = true;

        var svc = new AccountDeletionService(store, tokenStore, outbox, protector, new VerifiedDeletionOtpGateway());
        var act = () => svc.RequestDeletionAsync(user.Id, new AccountDeletionRequest(null, null, "123456"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be(ErrorCodes.Identity.HasActiveBookings);
    }

    [Fact]
    public async Task GetDeletionStatusAsync_ReturnsAccurateRetentionInfo()
    {
        var store = new InMemoryAccountDeletionStore();
        var tokenStore = new InMemoryRefreshTokenStore();
        var outbox = new InMemoryOutboxDispatcher();
        var protector = new InMemoryDeletionTokenProtector();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "pending@example.com",
            FullName = "Pending User",
            Status = UserStatus.PendingDeletion,
            DeletedAt = DateTime.UtcNow.AddDays(-5)
        };
        store.Users[user.Id] = user;

        var svc = new AccountDeletionService(store, tokenStore, outbox, protector, new VerifiedDeletionOtpGateway());
        var status = await svc.GetDeletionStatusAsync(user.Id, CancellationToken.None);

        status.IsPendingDeletion.Should().BeTrue();
        status.RetentionDaysRemaining.Should().Be(25);
    }

    [Fact]
    public async Task CancelDeletionAsync_RestoresActiveStatus()
    {
        var store = new InMemoryAccountDeletionStore();
        var tokenStore = new InMemoryRefreshTokenStore();
        var outbox = new InMemoryOutboxDispatcher();
        var protector = new InMemoryDeletionTokenProtector();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "cancelled@example.com",
            FullName = "Cancelled User",
            Status = UserStatus.Active,
            DeletionRequestedAt = DateTime.UtcNow.AddDays(-2)
        };
        store.Users[user.Id] = user;

        var svc = new AccountDeletionService(store, tokenStore, outbox, protector, new VerifiedDeletionOtpGateway());
        var status = await svc.CancelDeletionAsync(user.Id, CancellationToken.None);

        status.IsPendingDeletion.Should().BeFalse();
        status.Status.Should().Be("active");
        user.Status.Should().Be(UserStatus.Active);
        user.DeletedAt.Should().BeNull();
        user.DeletionRequestedAt.Should().BeNull();
        outbox.EnqueuedTypes.Should().Contain("user.deletion_cancelled");
    }

    [Fact]
    public async Task PublicDeletionFlow_RequestsToken_AndConfirmsDeletion()
    {
        var store = new InMemoryAccountDeletionStore();
        var tokenStore = new InMemoryRefreshTokenStore();
        var outbox = new InMemoryOutboxDispatcher();
        var protector = new InMemoryDeletionTokenProtector();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "public@example.com",
            NormalizedEmail = "PUBLIC@EXAMPLE.COM",
            FullName = "Public Form User",
            Status = UserStatus.Active
        };
        store.Users[user.Id] = user;

        var svc = new AccountDeletionService(store, tokenStore, outbox, protector, new VerifiedDeletionOtpGateway());

        // Step 1: Request public deletion
        await svc.RequestPublicDeletionAsync(new PublicAccountDeletionRequest("public@example.com", "Moving abroad"), CancellationToken.None);
        outbox.EnqueuedTypes.Should().Contain("account.deletion_verification_email");

        // Extract generated token from outbox payload
        var emailMsg = outbox.Messages.First(m => m.Type == "account.deletion_verification_email");
        var token = (string)emailMsg.Payload.GetType().GetProperty("Token")!.GetValue(emailMsg.Payload)!;
        token.Should().NotBeNullOrEmpty();

        // Step 2: Confirm public deletion with token
        var result = await svc.ConfirmPublicDeletionAsync(token, CancellationToken.None);
        result.IsPendingDeletion.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);
        user.DeletionRequestedAt.Should().NotBeNull();
        tokenStore.RevokedUserTokens.Should().NotContain(user.Id);
    }

    [Fact]
    public async Task ExportUserDataAsync_ReturnsSanitizedExportDto()
    {
        var store = new InMemoryAccountDeletionStore();
        var tokenStore = new InMemoryRefreshTokenStore();
        var outbox = new InMemoryOutboxDispatcher();
        var protector = new InMemoryDeletionTokenProtector();

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "export@example.com",
            FullName = "Export User",
            Timezone = "Asia/Ho_Chi_Minh",
            Status = UserStatus.Active
        };
        store.Users[user.Id] = user;

        var profile = UserProfile.Create(user.Id);
        profile.Update(phone: "0911223344", fitnessGoal: "Weight loss");
        store.Profiles[user.Id] = profile;
        store.Roles[user.Id] = "member";

        var svc = new AccountDeletionService(store, tokenStore, outbox, protector, new VerifiedDeletionOtpGateway());
        var export = await svc.ExportUserDataAsync(user.Id, CancellationToken.None);

        export.Should().NotBeNull();
        export.UserId.Should().Be(user.Id);
        export.Email.Should().Be("export@example.com");
        export.FullName.Should().Be("Export User");
        export.Profile.Should().NotBeNull();
        export.Profile!.FitnessGoal.Should().Be("Weight loss");
        export.Profile.Phone.Should().Be("0911223344");
    }
}

internal sealed class VerifiedDeletionOtpGateway : IAccountDeletionOtpGateway
{
    public Task<DeletionOtpStartResult> StartAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(new DeletionOtpStartResult(DateTime.UtcNow.AddMinutes(5), 60));
    public Task<AccountDeletionStatusDto> ExecuteVerifiedAsync(Guid userId, string code,
        Func<Task<AccountDeletionStatusDto>> action, CancellationToken ct) => code == "123456"
        ? action() : throw new DomainException("DELETION_OTP_INVALID", "Invalid OTP", 400);
}

internal class InMemoryDeletionTokenProtector : IDeletionTokenProtector
{
    public string Protect(string plaintext) => "protected_" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string protectedText)
    {
        if (!protectedText.StartsWith("protected_")) throw new FormatException("Invalid token format");
        var raw = protectedText.Substring("protected_".Length);
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw));
    }
}

internal class InMemoryAccountDeletionStore : IAccountDeletionStore
{
    public Task<T> WithUserLockAsync<T>(Guid userId, Func<Task<T>> action, CancellationToken ct) => action();
    public Dictionary<Guid, ApplicationUser> Users { get; } = new();
    public Dictionary<Guid, UserProfile> Profiles { get; } = new();
    public Dictionary<Guid, string> Roles { get; } = new();
    public Dictionary<Guid, bool> UserHasActiveBookings { get; } = new();
    public List<Guid> RevokedDeviceUsers { get; } = new();

    public Task<ApplicationUser?> FindUserByIdAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);
    }

    public Task<ApplicationUser?> FindIncludingPendingAsync(Guid userId, CancellationToken ct) =>
        FindUserByIdAsync(userId, ct);

    public Task<(IReadOnlyList<ApplicationUser> Items, int Total)> ListForAdminAsync(int page, int pageSize, CancellationToken ct)
    {
        var matches = Users.Values.Where(u => u.DeletionRequestedAt.HasValue || u.Status == UserStatus.PendingDeletion)
            .OrderBy(u => u.DeletionRequestedAt).ToList();
        return Task.FromResult<(IReadOnlyList<ApplicationUser>, int)>(
            (matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matches.Count));
    }

    public Task<IReadOnlyList<ApplicationUser>> ListDueForFinalizationAsync(DateTime cutoffUtc, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ApplicationUser>>(Users.Values
            .Where(u => u.Status == UserStatus.PendingDeletion && u.DeletedAt <= cutoffUtc)
            .Take(limit).ToList());

    public Task<ApplicationUser?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        var user = Users.Values.FirstOrDefault(u => string.Equals(u.NormalizedEmail, email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user);
    }

    public Task<bool> HasActiveBookingsAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(UserHasActiveBookings.TryGetValue(userId, out var has) && has);
    }

    public Task<UserProfile?> GetUserProfileAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Profiles.TryGetValue(userId, out var p) ? p : null);
    }

    public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Roles.TryGetValue(userId, out var r) ? r : null);
    }

    public Task RevokeUserDevicesAsync(Guid userId, CancellationToken ct)
    {
        RevokedDeviceUsers.Add(userId);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

internal class InMemoryOutboxDispatcher : IOutboxDispatcher
{
    public List<string> EnqueuedTypes { get; } = new();
    public List<(string Type, object Payload)> Messages { get; } = new();

    public void Enqueue(string type, object payload)
    {
        EnqueuedTypes.Add(type);
        Messages.Add((type, payload));
    }
}
