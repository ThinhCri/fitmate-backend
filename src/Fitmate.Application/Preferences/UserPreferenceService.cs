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
        ValidateNutritionTargets(request);
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
            request.BiometricLoginEnabled,
            request.TargetCalories,
            request.TargetProteinG,
            request.TargetCarbsG,
            request.TargetFatG);

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
        p.UpdatedAt,
        p.TargetCalories,
        p.TargetProteinG,
        p.TargetCarbsG,
        p.TargetFatG);

    private static void ValidateNutritionTargets(UpdateUserPreferenceRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.TargetCalories is <= 0 or > 10000) errors["target_calories"] = new[] { "must be > 0 and <= 10000" };
        if (request.TargetProteinG is < 0 or > 1000) errors["target_protein_g"] = new[] { "must be 0..1000" };
        if (request.TargetCarbsG is < 0 or > 1000) errors["target_carbs_g"] = new[] { "must be 0..1000" };
        if (request.TargetFatG is < 0 or > 1000) errors["target_fat_g"] = new[] { "must be 0..1000" };
        if (errors.Count > 0)
            throw new Fitmate.Domain.Common.ValidationException(
                Fitmate.Domain.Common.ErrorCodes.Validation.FieldInvalid,
                "Invalid nutrition targets.", errors);
    }
}

