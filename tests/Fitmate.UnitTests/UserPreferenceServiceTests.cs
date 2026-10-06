using Fitmate.Application.Preferences;
using Fitmate.Domain.Identity;
using FluentAssertions;

namespace Fitmate.UnitTests;

public class UserPreferenceServiceTests
{
    private sealed class InMemoryUserPreferenceStore : IUserPreferenceStore
    {
        public Dictionary<Guid, UserPreference> Items { get; } = new();

        public Task<UserPreference?> FindByUserIdAsync(Guid userId, CancellationToken ct)
        {
            return Task.FromResult(Items.Values.FirstOrDefault(x => x.UserId == userId));
        }

        public Task AddAsync(UserPreference preference, CancellationToken ct)
        {
            Items[preference.Id] = preference;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task GetByUserId_WhenNotFound_CreatesAndReturnsDefaultPreferences()
    {
        var store = new InMemoryUserPreferenceStore();
        var svc = new UserPreferenceService(store);
        var userId = Guid.NewGuid();

        var pref = await svc.GetByUserIdAsync(userId, CancellationToken.None);

        pref.Should().NotBeNull();
        pref.UserId.Should().Be(userId);
        pref.Language.Should().Be("vi");
        pref.Theme.Should().Be("system");
        pref.Timezone.Should().Be("Asia/Ho_Chi_Minh");
        pref.NotificationsEnabled.Should().BeTrue();
        pref.EmailNotifications.Should().BeTrue();
        pref.PushNotifications.Should().BeTrue();
        pref.HealthDataSharingConsent.Should().BeFalse();
        pref.BiometricLoginEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Update_WithNewValues_UpdatesAndPersistsPreferences()
    {
        var store = new InMemoryUserPreferenceStore();
        var svc = new UserPreferenceService(store);
        var userId = Guid.NewGuid();

        // First call creates defaults
        await svc.GetByUserIdAsync(userId, CancellationToken.None);

        var updateReq = new UpdateUserPreferenceRequest(
            Language: "en",
            Theme: "dark",
            Timezone: "America/New_York",
            NotificationsEnabled: true,
            EmailNotifications: false,
            PushNotifications: true,
            HealthDataSharingConsent: true,
            BiometricLoginEnabled: true,
            TargetCalories: 2100,
            TargetProteinG: 130,
            TargetCarbsG: 250,
            TargetFatG: 70);

        var updated = await svc.UpdateAsync(userId, updateReq, CancellationToken.None);

        updated.Language.Should().Be("en");
        updated.Theme.Should().Be("dark");
        updated.Timezone.Should().Be("America/New_York");
        updated.EmailNotifications.Should().BeFalse();
        updated.HealthDataSharingConsent.Should().BeTrue();
        updated.BiometricLoginEnabled.Should().BeTrue();
        updated.TargetCalories.Should().Be(2100);
        updated.TargetProteinG.Should().Be(130);
        updated.TargetCarbsG.Should().Be(250);
        updated.TargetFatG.Should().Be(70);

        // Ensure persistent across next retrieval (simulating relogin)
        var fetchedAgain = await svc.GetByUserIdAsync(userId, CancellationToken.None);
        fetchedAgain.Language.Should().Be("en");
        fetchedAgain.Theme.Should().Be("dark");
        fetchedAgain.HealthDataSharingConsent.Should().BeTrue();
    }
}

