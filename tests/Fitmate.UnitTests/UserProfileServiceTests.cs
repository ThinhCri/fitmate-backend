using Fitmate.Application.Identity;
using Fitmate.Application.Storage;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class UserProfileServiceTests
{
    [Fact]
    public async Task GetMyProfileAsync_ReturnsFullProfile_AndAutoCreatesIfMissing()
    {
        var store = new InMemoryUserProfileStore();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            FullName = "Nguyen Van A",
            Timezone = "Asia/Ho_Chi_Minh",
            CreatedAt = DateTime.UtcNow
        };
        store.Users[user.Id] = user;
        store.Roles[user.Id] = "member";

        var svc = new UserProfileService(store);
        var profileDto = await svc.GetMyProfileAsync(user.Id, CancellationToken.None);

        profileDto.Should().NotBeNull();
        profileDto.Email.Should().Be("user@test.com");
        profileDto.FullName.Should().Be("Nguyen Van A");
        profileDto.Role.Should().Be("member");
        store.Profiles.Should().ContainKey(user.Id); // Auto created
    }

    [Fact]
    public async Task UpdateMyProfileAsync_UpdatesAllowedFields()
    {
        var store = new InMemoryUserProfileStore();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            FullName = "Nguyen Van A",
            Timezone = "Asia/Ho_Chi_Minh",
            CreatedAt = DateTime.UtcNow
        };
        store.Users[user.Id] = user;
        store.Roles[user.Id] = "pt";

        var svc = new UserProfileService(store);
        var req = new UserProfileUpdateRequest(
            FullName: "Nguyen Van B",
            AvatarUrl: null,
            Phone: "0901234567",
            Gender: "male",
            Birthday: new DateOnly(1995, 5, 20),
            HeightCm: 175.5m,
            WeightKg: 70.0m,
            FitnessGoal: "Build muscle",
            TrainingDays: new List<string> { "Monday", "Wednesday", "Friday" },
            MedicalConditions: new List<string> { "Asthma" },
            Allergies: new List<string> { "Peanut" },
            Timezone: "Asia/Bangkok",
            Bio: "Huấn luyện bền vững",
            PreferredTimeSlots: new List<string> { "18:00-19:00" },
            HealthNotes: "Ưu tiên bài tập ít tác động");

        var updated = await svc.UpdateMyProfileAsync(user.Id, req, CancellationToken.None);

        updated.FullName.Should().Be("Nguyen Van B");
        updated.Timezone.Should().Be("Asia/Bangkok");
        updated.Phone.Should().Be("0901234567");
        updated.HeightCm.Should().Be(175.5m);
        updated.WeightKg.Should().Be(70.0m);
        updated.FitnessGoal.Should().Be("Build muscle");
        updated.Role.Should().Be("pt");
        updated.Bio.Should().Be("Huấn luyện bền vững");
        updated.PreferredTimeSlots.Should().ContainSingle("18:00-19:00");
        updated.HealthNotes.Should().Be("Ưu tiên bài tập ít tác động");
    }

    [Fact]
    public async Task UpdateMyProfileAsync_ThrowsValidationException_OnInvalidMetrics()
    {
        var store = new InMemoryUserProfileStore();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            FullName = "Nguyen Van A",
            CreatedAt = DateTime.UtcNow
        };
        store.Users[user.Id] = user;

        var svc = new UserProfileService(store);
        var invalidReq = new UserProfileUpdateRequest(
            FullName: null,
            AvatarUrl: null,
            Phone: null,
            Gender: null,
            Birthday: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), // Future date
            HeightCm: 350m, // > 300
            WeightKg: -5m, // <= 0
            FitnessGoal: null,
            TrainingDays: null,
            MedicalConditions: null,
            Allergies: null,
            Timezone: null);

        var act = () => svc.UpdateMyProfileAsync(user.Id, invalidReq, CancellationToken.None);
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetPublicProfileAsync_HidesSensitiveFields()
    {
        var store = new InMemoryUserProfileStore();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "secret@test.com",
            FullName = "Public Coach",
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        store.Users[user.Id] = user;
        store.Roles[user.Id] = "pt";

        var profile = UserProfile.Create(user.Id);
        profile.Update(
            phone: "0999999999",
            gender: Gender.Male,
            birthday: new DateOnly(1990, 1, 1),
            heightCm: 180,
            weightKg: 80,
            fitnessGoal: "Endurance",
            trainingDays: new List<string> { "Mon" },
            medicalConditions: new List<string> { "Heart condition" },
            allergies: new List<string> { "Latex" });
        store.Profiles[user.Id] = profile;

        var svc = new UserProfileService(store);
        var publicDto = await svc.GetPublicProfileAsync(user.Id, CancellationToken.None);

        publicDto.Should().NotBeNull();
        publicDto.Id.Should().Be(user.Id);
        publicDto.FullName.Should().Be("Public Coach");
        publicDto.Role.Should().Be("pt");
        publicDto.FitnessGoal.Should().Be("Endurance");
        // Public profile record has no email, phone, height, weight, medical conditions, allergies fields.
    }

    [Fact]
    public async Task GetPublicProfileAsync_ThrowsNotFound_WhenUserLockedOrDeleted()
    {
        var store = new InMemoryUserProfileStore();
        var lockedUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "locked@test.com",
            FullName = "Locked User",
            Status = UserStatus.Locked,
            CreatedAt = DateTime.UtcNow
        };
        store.Users[lockedUser.Id] = lockedUser;

        var svc = new UserProfileService(store);
        var act = () => svc.GetPublicProfileAsync(lockedUser.Id, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SearchUsersAsync_ReturnsMatchingPublicProfiles()
    {
        var store = new InMemoryUserProfileStore();
        var user1 = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Trainer Alice", Status = UserStatus.Active };
        var user2 = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Bob Member", Status = UserStatus.Active };
        store.Users[user1.Id] = user1;
        store.Users[user2.Id] = user2;
        store.Roles[user1.Id] = "pt";
        store.Roles[user2.Id] = "member";

        var svc = new UserProfileService(store);
        var results = await svc.SearchUsersAsync(Guid.NewGuid(), "Alice", 10, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].FullName.Should().Be("Trainer Alice");
        results[0].Role.Should().Be("pt");
    }

    [Fact]
    public async Task AttachAvatarAsync_PersistsOwnedStorageKey_AndReturnsFreshUrl()
    {
        var store = new InMemoryUserProfileStore();
        var user = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Member", Email = "member@test.com" };
        store.Users[user.Id] = user;
        var key = $"avatars/{user.Id:N}_avatar_abc.jpg";
        var svc = new UserProfileService(store, new FakeSignedUrlService());

        var result = await svc.AttachAvatarAsync(user.Id, new AttachAvatarRequest(key), CancellationToken.None);

        user.AvatarStorageKey.Should().Be(key);
        user.AvatarUrl.Should().BeNull();
        result.AvatarUrl.Should().Be($"https://media.test/{key}?signed=1");
    }

    [Fact]
    public async Task AttachAvatarAsync_RejectsStorageKeyOwnedByAnotherUser()
    {
        var store = new InMemoryUserProfileStore();
        var user = new ApplicationUser { Id = Guid.NewGuid(), FullName = "Member" };
        store.Users[user.Id] = user;
        var svc = new UserProfileService(store, new FakeSignedUrlService());

        var act = () => svc.AttachAvatarAsync(user.Id,
            new AttachAvatarRequest($"avatars/{Guid.NewGuid():N}_avatar.jpg"), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}

internal sealed class FakeSignedUrlService : ISignedUrlService
{
    public Uri CreatePresignedReadUrl(string relativePath, TimeSpan ttl) =>
        new($"https://media.test/{relativePath}?signed=1");

    public string? TryValidateAndExtractPath(string pathAndQuery) => null;
}

internal class InMemoryUserProfileStore : IUserProfileStore
{
    public Dictionary<Guid, ApplicationUser> Users { get; } = new();
    public Dictionary<Guid, UserProfile> Profiles { get; } = new();
    public Dictionary<Guid, string> Roles { get; } = new();

    public Task<ApplicationUser?> FindUserByIdAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Users.TryGetValue(userId, out var u) ? u : null);
    }

    public Task<UserProfile?> FindProfileByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Profiles.TryGetValue(userId, out var p) ? p : null);
    }

    public Task AddProfileAsync(UserProfile profile, CancellationToken ct)
    {
        Profiles[profile.UserId] = profile;
        return Task.CompletedTask;
    }

    public Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Roles.TryGetValue(userId, out var r) ? r : null);
    }

    public Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> SearchUsersAsync(Guid viewerId, string query, int limit, CancellationToken ct)
    {
        var matches = Users.Values
            .Where(u => !u.IsDeleted && u.Status != UserStatus.Locked && u.FullName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .Select(u => (
                User: u,
                Profile: Profiles.TryGetValue(u.Id, out var p) ? p : null,
                Role: Roles.TryGetValue(u.Id, out var r) ? r : "member"))
            .ToList();

        return Task.FromResult<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>>(matches);
    }

    public Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> ListSuggestedUsersAsync(Guid viewerId, int limit, CancellationToken ct)
        => SearchUsersAsync(viewerId, string.Empty, limit, ct);

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
