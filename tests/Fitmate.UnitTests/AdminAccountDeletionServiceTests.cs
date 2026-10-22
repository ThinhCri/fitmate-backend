using Fitmate.Application.Auth;
using Fitmate.Application.Identity;
using Fitmate.Application.Trainers;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using FluentAssertions;
using Moq;

namespace Fitmate.UnitTests;

public sealed class AdminAccountDeletionServiceTests
{
    private readonly InMemoryAccountDeletionStore _store = new();
    private readonly InMemoryRefreshTokenStore _tokens = new();
    private readonly Mock<IAuthorizationGuard> _guard = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly Guid _admin = Guid.NewGuid();

    private AdminAccountDeletionService Service() => new(_store, _tokens, _guard.Object, _audit.Object,
        new AccountDeletionFinalizationGate(true));

    [Fact]
    public async Task StartCountdown_WhenFinalizerIsNotReady_LeavesAccountUntouched()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "gated@example.com", FullName = "G",
            DeletionRequestedAt = DateTime.UtcNow };
        _store.Users[user.Id] = user;
        var service = new AdminAccountDeletionService(_store, _tokens, _guard.Object, _audit.Object,
            new AccountDeletionFinalizationGate(false));

        await FluentActions.Invoking(() => service.StartCountdownAsync(_admin, user.Id, CancellationToken.None))
            .Should().ThrowAsync<DomainException>();
        user.Status.Should().Be(UserStatus.Active);
        user.DeletedAt.Should().BeNull();
        _tokens.RevokedUserTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task StartCountdown_RequiresRequest_ThenRevokesSessions()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "a@example.com", FullName = "A", Status = UserStatus.Active };
        _store.Users[user.Id] = user;
        var service = Service();

        await FluentActions.Invoking(() => service.StartCountdownAsync(_admin, user.Id, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();

        user.DeletionRequestedAt = DateTime.UtcNow;
        var result = await service.StartCountdownAsync(_admin, user.Id, CancellationToken.None);
        result.Status.Should().Be("pending_deletion");
        result.DeleteAfterUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        user.Status.Should().Be(UserStatus.PendingDeletion);
        _tokens.RevokedUserTokens.Should().Contain(user.Id);
        _store.RevokedDeviceUsers.Should().Contain(user.Id);
    }

    [Fact]
    public async Task Restore_WithinGrace_ReleasesAccount()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "b@example.com", FullName = "B",
            Status = UserStatus.PendingDeletion, DeletedAt = DateTime.UtcNow.AddDays(-5), DeletionRequestedAt = DateTime.UtcNow.AddDays(-7) };
        _store.Users[user.Id] = user;

        var result = await Service().RestoreAsync(_admin, user.Id, "Owner requested reactivation", CancellationToken.None);
        result.Status.Should().Be("active");
        user.Status.Should().Be(UserStatus.Active);
        user.DeletedAt.Should().BeNull();
        user.DeletionRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task Restore_AfterGrace_IsRejected()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "c@example.com", FullName = "C",
            Status = UserStatus.PendingDeletion, DeletedAt = DateTime.UtcNow.AddDays(-31) };
        _store.Users[user.Id] = user;
        await FluentActions.Invoking(() => Service().RestoreAsync(_admin, user.Id, "Owner requested reactivation", CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task StartCountdown_RejectsAdminAndFinancialObligations()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "d@example.com", FullName = "D",
            DeletionRequestedAt = DateTime.UtcNow };
        _store.Users[user.Id] = user;
        _store.Roles[user.Id] = "Admin";
        await FluentActions.Invoking(() => Service().StartCountdownAsync(_admin, user.Id, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
        _store.Roles[user.Id] = "Member";
        _store.UserHasActiveBookings[user.Id] = true;
        await FluentActions.Invoking(() => Service().StartCountdownAsync(_admin, user.Id, CancellationToken.None))
            .Should().ThrowAsync<ConflictException>();
    }
}
