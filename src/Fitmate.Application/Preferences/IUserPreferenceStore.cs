using Fitmate.Domain.Identity;

namespace Fitmate.Application.Preferences;

public interface IUserPreferenceStore
{
    Task<UserPreference?> FindByUserIdAsync(Guid userId, CancellationToken ct);
    Task AddAsync(UserPreference preference, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

