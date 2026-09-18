using Fitmate.Domain.Common;
using Fitmate.Domain.Trainers;

namespace Fitmate.Application.Trainers;

/// <summary>
/// Default <see cref="IPtProfileService"/> impl dùng <see cref="IPtProfileStore"/> + <see cref="IUserRoleLookup"/>.
/// </summary>
public class PtProfileService : IPtProfileService
{
    private readonly IPtProfileStore _store;
    private readonly IUserRoleLookup _users;

    public PtProfileService(IPtProfileStore store, IUserRoleLookup users)
    {
        _store = store;
        _users = users;
    }

    public async Task<PtProfile> EnsureDraftAsync(Guid userId, CancellationToken ct)
    {
        var existing = await _store.FindByUserIdAsync(userId, ct);
        if (existing is not null) return existing;

        if (!await _users.UserExistsAsync(userId, ct))
            throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");
        if (!await _users.IsInRoleAsync(userId, "PT", ct))
            throw new ForbiddenException(ErrorCodes.Identity.Forbidden, "User is not a PT.");

        var profile = PtProfile.CreateDraft(userId, specialty: null, experienceYears: null, pricePerSessionVnd: null, bio: null);
        await _store.AddAsync(profile, ct);
        await _store.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<PtProfile> UpdateAsync(Guid userId, string? specialty, int? experienceYears, long? pricePerSessionVnd, string? bio, CancellationToken ct)
    {
        ValidateOptionalFields(experienceYears, pricePerSessionVnd, bio);

        var profile = await _store.FindByUserIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.General.NotFound, $"PT profile not found for user {userId}. Call EnsureDraftAsync first.");

        profile.Update(specialty, experienceYears, pricePerSessionVnd, bio);
        await _store.SaveChangesAsync(ct);
        return profile;
    }

    public Task<PtProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        return _store.FindByUserIdAsync(userId, ct);
    }

    private static void ValidateOptionalFields(int? experienceYears, long? pricePerSessionVnd, string? bio)
    {
        if (experienceYears is < 0 or > 70)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "experienceYears out of range",
                new Dictionary<string, string[]> { ["experienceYears"] = new[] { "0..70" } });
        if (pricePerSessionVnd is < 0)
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "pricePerSessionVnd must be >= 0",
                new Dictionary<string, string[]> { ["pricePerSessionVnd"] = new[] { ">= 0" } });
        if (bio is { Length: > 2000 })
            throw new ValidationException(
                ErrorCodes.Validation.FieldInvalid,
                "bio too long",
                new Dictionary<string, string[]> { ["bio"] = new[] { "max 2000 chars" } });
    }
}
