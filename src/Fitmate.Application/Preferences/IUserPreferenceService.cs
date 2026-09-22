namespace Fitmate.Application.Preferences;

public interface IUserPreferenceService
{
    Task<UserPreferenceDto> GetByUserIdAsync(Guid userId, CancellationToken ct);
    Task<UserPreferenceDto> UpdateAsync(Guid userId, UpdateUserPreferenceRequest request, CancellationToken ct);
}

