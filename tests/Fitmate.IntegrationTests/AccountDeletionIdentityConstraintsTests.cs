using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.IntegrationTests.Infrastructure;
using Fitmate.Infrastructure.Persistence;
using Fitmate.Infrastructure.Identity;
using Fitmate.Application.Storage;
using Fitmate.Domain.Booking;
using Fitmate.Domain.Payment;
using Fitmate.Domain.Social;
using Fitmate.Domain.Trainers;
using Fitmate.Domain.Nutrition;
using Fitmate.Domain.Workout;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fitmate.IntegrationTests;

public sealed class AccountDeletionIdentityConstraintsTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;
    public AccountDeletionIdentityConstraintsTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public void CurrentModel_MatchesMigrationSnapshot()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var differ = db.GetService<IMigrationsModelDiffer>();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        snapshot.Should().NotBeNull();
        var initializedSnapshot = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot!, designTime: true);
        var differences = differ.GetDifferences(initializedSnapshot.GetRelationalModel(),
            db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
        differences.Should().BeEmpty(because: string.Join(", ", differences.Select(x => x.GetType().Name + ":" + (x as MigrationOperation)?.ToString())));
    }

    [Fact]
    public async Task FreshDatabase_MigrationChain_IncludesDeletionAndPhoneConstraints()
    {
        var databaseName = "fitmate_it_mig_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder
        {
            Host = FitmateTestAppFactory.DbHost,
            Port = int.Parse(FitmateTestAppFactory.DbPort),
            Database = "postgres",
            Username = FitmateTestAppFactory.DbUser,
            Password = FitmateTestAppFactory.DbPassword,
        };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync();
        }

        var target = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = databaseName };
        var options = new DbContextOptionsBuilder<FitmateDbContext>()
            .UseNpgsql(target.ConnectionString)
            .Options;
        await using var db = new FitmateDbContext(options);
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        applied.Should().Contain("20261021132000_AdminDeletionLifecycle");
        applied.Should().Contain("20261021133000_UniqueContactPhone");
        // The isolated test database is retained for diagnosis, never reused or dropped.
    }

    [Fact]
    public async Task PendingAccountReservesEmailAndPhone_UntilIdentityIsFinalized()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var email = $"deletion-{firstId:N}@example.com";
        var phone = "09" + (BitConverter.ToUInt32(firstId.ToByteArray(), 0) % 100_000_000).ToString("D8");
        var first = new ApplicationUser
        {
            Id = firstId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), FullName = "First",
            Status = UserStatus.PendingDeletion, DeletedAt = DateTime.UtcNow,
            DeletionRequestedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(first);
        var profile = UserProfile.Create(firstId);
        profile.SetContactPhone(phone);
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();

        var second = new ApplicationUser
        {
            Id = secondId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            UserName = $"second-{secondId:N}", NormalizedUserName = $"SECOND-{secondId:N}",
            FullName = "Second", Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(second);
        await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        db.Entry(second).State = EntityState.Detached;

        first.Status = UserStatus.Deleted;
        first.Email = null;
        first.NormalizedEmail = null;
        first.UserName = $"deleted-{firstId:N}";
        first.NormalizedUserName = $"DELETED-{firstId:N}";
        db.UserProfiles.Remove(profile);
        await db.SaveChangesAsync();

        db.Users.Add(second);
        var newProfile = UserProfile.Create(secondId);
        newProfile.SetContactPhone(phone);
        db.UserProfiles.Add(newProfile);
        await db.SaveChangesAsync();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == secondId)).Should().BeTrue();
    }

    [Fact]
    public async Task DueAccount_FinalizerScrubsIdentity_AndFreesContactForReuse()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var userId = Guid.NewGuid();
        var email = $"erase-{userId:N}@example.com";
        var phone = "09" + (BitConverter.ToUInt32(userId.ToByteArray(), 0) % 100_000_000).ToString("D8");
        var user = new ApplicationUser
        {
            Id = userId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), FullName = "Private Name",
            Status = UserStatus.PendingDeletion, DeletedAt = DateTime.UtcNow.AddDays(-31),
            DeletionRequestedAt = DateTime.UtcNow.AddDays(-32),
            CreatedAt = DateTime.UtcNow.AddDays(-60), UpdatedAt = DateTime.UtcNow.AddDays(-31)
        };
        db.Users.Add(user);
        var profile = UserProfile.Create(userId);
        profile.SetContactPhone(phone);
        db.UserProfiles.Add(profile);
        await db.SaveChangesAsync();

        var storage = new TestOwnedStorage();
        var finalizer = new AccountDeletionFinalizer(db, storage, storage, Options.Create(new StorageOptions()));
        (await finalizer.FinalizeAsync(userId, DateTime.UtcNow, CancellationToken.None)).Should().BeTrue();
        (await finalizer.FinalizeAsync(userId, DateTime.UtcNow, CancellationToken.None)).Should().BeFalse();

        db.ChangeTracker.Clear();
        var tombstone = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == userId);
        tombstone.Status.Should().Be(UserStatus.Deleted);
        tombstone.Email.Should().BeNull();
        tombstone.PhoneNumber.Should().BeNull();
        tombstone.FullName.Should().Be("[deleted]");
        (await db.UserProfiles.IgnoreQueryFilters().AnyAsync(x => x.UserId == userId)).Should().BeFalse();

        var replacementId = Guid.NewGuid();
        var replacement = new ApplicationUser
        {
            Id = replacementId, Email = email, NormalizedEmail = email.ToUpperInvariant(),
            UserName = email, NormalizedUserName = email.ToUpperInvariant(), FullName = "Replacement",
            Status = UserStatus.Active, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(replacement);
        var replacementProfile = UserProfile.Create(replacementId);
        replacementProfile.SetContactPhone(phone);
        db.UserProfiles.Add(replacementProfile);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task StorageFailure_KeepsPendingAccountForRetry()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = id, FullName = "Retry Me", UserName = $"retry-{id:N}",
            NormalizedUserName = $"RETRY-{id:N}", Status = UserStatus.PendingDeletion,
            DeletedAt = DateTime.UtcNow.AddDays(-31), CreatedAt = DateTime.UtcNow.AddDays(-60),
            UpdatedAt = DateTime.UtcNow.AddDays(-31)
        });
        await db.SaveChangesAsync();
        var storage = new TestOwnedStorage { FailDelete = true, OwnedKeys = [$"avatars/{id:N}_private.png"] };
        var finalizer = new AccountDeletionFinalizer(db, storage, storage, Options.Create(new StorageOptions()));
        await FluentActions.Invoking(() => finalizer.FinalizeAsync(id, DateTime.UtcNow, CancellationToken.None))
            .Should().ThrowAsync<IOException>();
        db.ChangeTracker.Clear();
        (await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).Status
            .Should().Be(UserStatus.PendingDeletion);
    }

    [Fact]
    public async Task DueAccount_RedactsSocialAndPaymentEvidence_WithoutChangingAmounts()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var userId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = userId, FullName = "Sensitive Name", UserName = $"person-{userId:N}",
            NormalizedUserName = $"PERSON-{userId:N}", Status = UserStatus.PendingDeletion,
            DeletedAt = DateTime.UtcNow.AddDays(-31), CreatedAt = DateTime.UtcNow.AddDays(-60),
            UpdatedAt = DateTime.UtcNow.AddDays(-31)
        });
        var key = $"posts/{userId:N}_photo_{Guid.NewGuid():N}.png";
        var post = Post.Create(userId, "private story", key, "https://example.com/private",
            ["sensitive"], PostVisibility.Public, false);
        db.Posts.Add(post);
        var certificateKey = $"certificates/{userId:N}_scan_{Guid.NewGuid():N}.png";
        db.Certificates.Add(Certificate.Submit(userId, "Private certificate", "Private issuer", "private-code",
            new DateOnly(2025, 1, 1), certificateKey, userId));
        db.WeightLogs.Add(WeightLog.Create(userId, 70.5m));
        db.MealLogs.Add(MealLog.Create(userId, "private food", null, 200, 10, 20, 5,
            null, MealSource.Manual, null, true, DateTime.UtcNow));
        var appointment = Appointment.CreateHold(userId, Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddDays(-45), DateTime.UtcNow.AddDays(-45), 60, 120000);
        db.Appointments.Add(appointment);
        var payment = Payment.CreatePending(appointment.Id, PaymentMethod.Payos, 120000,
            "https://example.com/private-checkout");
        payment.MarkPaid("provider-reference");
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE appointments SET appointment_status={(int)AppointmentStatus.Completed} WHERE id={appointment.Id}");
        db.ChangeTracker.Clear();

        var storage = new TestOwnedStorage { OwnedKeys = [key, certificateKey] };
        var finalizer = new AccountDeletionFinalizer(db, storage, storage, Options.Create(new StorageOptions()));
        (await finalizer.FinalizeAsync(userId, DateTime.UtcNow, CancellationToken.None)).Should().BeTrue();
        storage.DeletedKeys.Should().Contain(key);
        storage.DeletedKeys.Should().Contain(certificateKey);
        db.ChangeTracker.Clear();
        var redactedPost = await db.Posts.IgnoreQueryFilters().SingleAsync(x => x.Id == post.Id);
        redactedPost.Content.Should().Be("[deleted]");
        redactedPost.ImageStorageKey.Should().BeNull();
        redactedPost.ImageStorageKeys.Should().BeEmpty();
        var retainedPayment = await db.Payments.IgnoreQueryFilters().SingleAsync(x => x.Id == payment.Id);
        retainedPayment.AmountVnd.Should().Be(120000);
        retainedPayment.ProviderTxnRef.Should().Be("provider-reference");
        retainedPayment.RedirectUrl.Should().BeNull();
        (await db.Certificates.IgnoreQueryFilters().AnyAsync(x => x.PtUserId == userId)).Should().BeFalse();
        (await db.WeightLogs.IgnoreQueryFilters().AnyAsync(x => x.UserId == userId)).Should().BeFalse();
        (await db.MealLogs.IgnoreQueryFilters().AnyAsync(x => x.UserId == userId)).Should().BeFalse();
    }

    [Fact]
    public async Task GracePeriodNotExpired_DoesNotEraseAccountOrMedia()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = id, FullName = "Still Restorable", UserName = $"grace-{id:N}",
            NormalizedUserName = $"GRACE-{id:N}", Status = UserStatus.PendingDeletion,
            DeletedAt = DateTime.UtcNow.AddDays(-29), CreatedAt = DateTime.UtcNow.AddDays(-60),
            UpdatedAt = DateTime.UtcNow.AddDays(-29)
        });
        await db.SaveChangesAsync();
        var storage = new TestOwnedStorage { OwnedKeys = [$"avatars/{id:N}_portrait.png"] };
        var finalizer = new AccountDeletionFinalizer(db, storage, storage, Options.Create(new StorageOptions()));
        (await finalizer.FinalizeAsync(id, DateTime.UtcNow, CancellationToken.None)).Should().BeFalse();
        storage.DeletedKeys.Should().BeEmpty();
        db.ChangeTracker.Clear();
        (await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).Status
            .Should().Be(UserStatus.PendingDeletion);
    }

    [Theory]
    [InlineData(AppointmentStatus.AwaitingMemberConfirmation)]
    [InlineData(AppointmentStatus.Disputed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task UnsettledAppointment_PreventsFinalization(AppointmentStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var id = Guid.NewGuid();
        db.Users.Add(new ApplicationUser
        {
            Id = id, FullName = "Booking Owner", UserName = $"booking-{id:N}",
            NormalizedUserName = $"BOOKING-{id:N}", Status = UserStatus.PendingDeletion,
            DeletedAt = DateTime.UtcNow.AddDays(-31), CreatedAt = DateTime.UtcNow.AddDays(-60),
            UpdatedAt = DateTime.UtcNow.AddDays(-31)
        });
        var appointment = Appointment.CreateHold(id, Guid.NewGuid(), Guid.NewGuid(),
            DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-40), 60, 90000);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE appointments SET appointment_status={(int)status} WHERE id={appointment.Id}");
        db.ChangeTracker.Clear();
        var storage = new TestOwnedStorage();
        var finalizer = new AccountDeletionFinalizer(db, storage, storage, Options.Create(new StorageOptions()));
        await FluentActions.Invoking(() => finalizer.FinalizeAsync(id, DateTime.UtcNow, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Clear();
        (await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).Status
            .Should().Be(UserStatus.PendingDeletion);
    }

    [Fact]
    public async Task AdminMutationLock_SerializesCompetingAccountTransitions()
    {
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<FitmateDbContext>();
        var id = Guid.NewGuid();
        firstDb.Users.Add(new ApplicationUser
        {
            Id = id, FullName = "Lock Owner", UserName = $"lock-{id:N}",
            NormalizedUserName = $"LOCK-{id:N}", Status = UserStatus.PendingDeletion,
            DeletedAt = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await firstDb.SaveChangesAsync();
        var firstStore = new EfAccountDeletionStore(firstDb);
        var secondStore = new EfAccountDeletionStore(secondDb);
        var lockAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = firstStore.WithUserLockAsync(id, async () =>
        {
            lockAcquired.SetResult();
            await releaseLock.Task;
            return true;
        }, CancellationToken.None);
        await lockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Task.Run(async () =>
        {
            secondEntered.SetResult();
            return await secondStore.WithUserLockAsync(id, () => Task.FromResult(true), CancellationToken.None);
        });
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Task.Delay(200);
            second.IsCompleted.Should().BeFalse("the first transaction still owns the user row lock");
        }
        finally
        {
            releaseLock.SetResult();
        }
        (await first.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        (await second.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    private sealed class TestOwnedStorage : IStorageService, IOwnedStorageKeyListing
    {
        public bool FailDelete { get; init; }
        public IReadOnlyList<string> OwnedKeys { get; init; } = [];
        public List<string> DeletedKeys { get; } = [];
        public Task<IReadOnlyList<string>> ListOwnedKeysAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(OwnedKeys);
        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            if (FailDelete) throw new IOException("Simulated storage failure");
            DeletedKeys.Add(relativePath);
            return Task.CompletedTask;
        }
        public Task<Uri> UploadAsync(string relativePath, Stream data, string contentType, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
