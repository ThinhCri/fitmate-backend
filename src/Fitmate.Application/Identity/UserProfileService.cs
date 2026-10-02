using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;

namespace Fitmate.Application.Identity;

public class UserProfileService : IUserProfileService
{
    private readonly IUserProfileStore _store;

    public UserProfileService(IUserProfileStore store)
    {
        _store = store;
    }

    public async Task<UserProfileDto> GetMyProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        var profile = await _store.FindProfileByUserIdAsync(userId, ct);
        if (profile is null)
        {
            profile = UserProfile.Create(userId);
            await _store.AddProfileAsync(profile, ct);
            await _store.SaveChangesAsync(ct);
        }

        var role = await _store.GetPrimaryRoleAsync(userId, ct) ?? "member";
        return MapToDto(user, profile, role);
    }

    public async Task<UserProfileDto> UpdateMyProfileAsync(Guid userId, UserProfileUpdateRequest request, CancellationToken ct)
    {
        ValidateUpdateRequest(request);

        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName.Trim();

        if (!string.IsNullOrWhiteSpace(request.Timezone))
            user.Timezone = request.Timezone.Trim();

        if (request.AvatarUrl is not null)
            user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();

        user.UpdatedAt = DateTime.UtcNow;

        var profile = await _store.FindProfileByUserIdAsync(userId, ct);
        if (profile is null)
        {
            profile = UserProfile.Create(userId);
            await _store.AddProfileAsync(profile, ct);
        }

        Gender? gender = null;
        if (!string.IsNullOrWhiteSpace(request.Gender) && Enum.TryParse<Gender>(request.Gender, true, out var parsedGender))
        {
            gender = parsedGender;
        }

        profile.Update(
            phone: request.Phone,
            bio: request.Bio,
            gender: gender,
            birthday: request.Birthday,
            heightCm: request.HeightCm,
            weightKg: request.WeightKg,
            fitnessGoal: request.FitnessGoal,
            trainingDays: request.TrainingDays,
            medicalConditions: request.MedicalConditions,
            allergies: request.Allergies,
            preferredTimeSlots: request.PreferredTimeSlots,
            healthNotes: request.HealthNotes);

        await _store.SaveChangesAsync(ct);

        var role = await _store.GetPrimaryRoleAsync(userId, ct) ?? "member";
        return MapToDto(user, profile, role);
    }

    public async Task<PublicUserProfileDto> GetPublicProfileAsync(Guid targetUserId, CancellationToken ct)
    {
        var user = await _store.FindUserByIdAsync(targetUserId, ct);
        if (user is null || user.IsDeleted || user.Status == UserStatus.Locked)
            throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {targetUserId} not found.");

        var profile = await _store.FindProfileByUserIdAsync(targetUserId, ct);
        var role = await _store.GetPrimaryRoleAsync(targetUserId, ct) ?? "member";

        return new PublicUserProfileDto(
            Id: user.Id,
            FullName: user.FullName,
            AvatarUrl: user.AvatarUrl,
            Role: role.ToLowerInvariant(),
            FitnessGoal: profile?.FitnessGoal,
            CreatedAtUtc: user.CreatedAt);
    }

    public async Task<IReadOnlyList<PublicUserProfileDto>> SearchUsersAsync(string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<PublicUserProfileDto>();

        var clampedLimit = Math.Clamp(limit, 1, 50);
        var results = await _store.SearchUsersAsync(query.Trim(), clampedLimit, ct);

        return results.Select(r => new PublicUserProfileDto(
            Id: r.User.Id,
            FullName: r.User.FullName,
            AvatarUrl: r.User.AvatarUrl,
            Role: r.Role.ToLowerInvariant(),
            FitnessGoal: r.Profile?.FitnessGoal,
            CreatedAtUtc: r.User.CreatedAt)).ToList();
    }

    private static void ValidateUpdateRequest(UserProfileUpdateRequest req)
    {
        if (req.HeightCm.HasValue && (req.HeightCm.Value <= 0 || req.HeightCm.Value > 300))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Height must be between 1 and 300 cm.",
                new Dictionary<string, string[]> { ["height_cm"] = new[] { "Height must be between 1 and 300 cm." } });

        if (req.WeightKg.HasValue && (req.WeightKg.Value <= 0 || req.WeightKg.Value > 500))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Weight must be between 1 and 500 kg.",
                new Dictionary<string, string[]> { ["weight_kg"] = new[] { "Weight must be between 1 and 500 kg." } });

        if (req.Birthday.HasValue && req.Birthday.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Birthday cannot be in the future.",
                new Dictionary<string, string[]> { ["birthday"] = new[] { "Birthday cannot be in the future." } });

        if (req.Bio?.Length > 500)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Bio must not exceed 500 characters.",
                new Dictionary<string, string[]> { ["bio"] = new[] { "Bio must not exceed 500 characters." } });

        if (req.HealthNotes?.Length > 2000)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Health notes must not exceed 2000 characters.",
                new Dictionary<string, string[]> { ["health_notes"] = new[] { "Health notes must not exceed 2000 characters." } });

        if (req.PreferredTimeSlots?.Count > 12 || req.PreferredTimeSlots?.Any(x => x.Length > 32) == true)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Preferred time slots are invalid.",
                new Dictionary<string, string[]> { ["preferred_time_slots"] = new[] { "Preferred time slots are invalid." } });

        if (req.AvatarUrl?.Length > 500)
            throw new ValidationException(ErrorCodes.Validation.FieldInvalid, "Avatar URL must not exceed 500 characters.",
                new Dictionary<string, string[]> { ["avatar_url"] = new[] { "Avatar URL must not exceed 500 characters." } });
    }

    private static UserProfileDto MapToDto(ApplicationUser user, UserProfile profile, string role)
    {
        return new UserProfileDto(
            Id: user.Id,
            Email: user.Email ?? "",
            FullName: user.FullName,
            AvatarUrl: user.AvatarUrl,
            Phone: profile.Phone,
            Gender: profile.Gender?.ToString().ToLowerInvariant(),
            Birthday: profile.Birthday,
            HeightCm: profile.HeightCm,
            WeightKg: profile.WeightKg,
            FitnessGoal: profile.FitnessGoal,
            TrainingDays: profile.TrainingDays ?? new List<string>(),
            MedicalConditions: profile.MedicalConditions ?? new List<string>(),
            Allergies: profile.Allergies ?? new List<string>(),
            Timezone: user.Timezone,
            Role: role.ToLowerInvariant(),
            Bio: profile.Bio,
            PreferredTimeSlots: profile.PreferredTimeSlots ?? new List<string>(),
            HealthNotes: profile.HealthNotes);
    }
}

