using Fitmate.Domain.Identity;

namespace Fitmate.Application.Preferences;

public class UserPreferenceService : IUserPreferenceService
{
    private readonly IUserPreferenceStore _store;

    public UserPreferenceService(IUserPreferenceStore store)
    {
        _store = store;
    }

    public async Task<UserPreferenceDto> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var pref = await _store.FindByUserIdAsync(userId, ct);
        if (pref is null)
        {
            pref = UserPreference.CreateDefault(userId);
            await _store.AddAsync(pref, ct);
            await _store.SaveChangesAsync(ct);
        }

        return MapToDto(pref);
    }

    public async Task<UserPreferenceDto> UpdateAsync(Guid userId, UpdateUserPreferenceRequest request, CancellationToken ct)
    {
        var pref = await _store.FindByUserIdAsync(userId, ct);
        if (pref is null)
        {
            pref = UserPreference.CreateDefault(userId);
            await _store.AddAsync(pref, ct);
        }

        pref.Update(
            request.Language,
            request.Theme,
            request.Timezone,
            request.NotificationsEnabled,
            request.EmailNotifications,
            request.PushNotifications,
            request.HealthDataSharingConsent,
            request.BiometricLoginEnabled);

        await _store.SaveChangesAsync(ct);
        return MapToDto(pref);
    }

    private static UserPreferenceDto MapToDto(UserPreference p) => new(
        p.UserId,
        p.Language,
        p.Theme,
        p.Timezone,
        p.NotificationsEnabled,
        p.EmailNotifications,
        p.PushNotifications,
        p.HealthDataSharingConsent,
        p.BiometricLoginEnabled,
        p.UpdatedAt);
}

