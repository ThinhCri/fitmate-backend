using Fitmate.Domain.Identity;

namespace Fitmate.Application.Identity;

public interface IUserProfileService
{
    Task<UserProfileDto> GetMyProfileAsync(Guid userId, CancellationToken ct);
    Task<UserProfileDto> UpdateMyProfileAsync(Guid userId, UserProfileUpdateRequest request, CancellationToken ct);
    Task<UserProfileDto> AttachAvatarAsync(Guid userId, AttachAvatarRequest request, CancellationToken ct);
    Task<PublicUserProfileDto> GetPublicProfileAsync(Guid targetUserId, CancellationToken ct);
    Task<IReadOnlyList<PublicUserProfileDto>> SearchUsersAsync(Guid viewerId, string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<PublicUserProfileDto>> ListSuggestedUsersAsync(Guid viewerId, int limit, CancellationToken ct);
}

public interface IUserProfileStore
{
    Task<ApplicationUser?> FindUserByIdAsync(Guid userId, CancellationToken ct);
    Task<UserProfile?> FindProfileByUserIdAsync(Guid userId, CancellationToken ct);
    Task<bool> IsPhoneInUseAsync(Guid userId, string phone, CancellationToken ct);
    Task AddProfileAsync(UserProfile profile, CancellationToken ct);
    Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> SearchUsersAsync(Guid viewerId, string query, int limit, CancellationToken ct);
    Task<IReadOnlyList<(ApplicationUser User, UserProfile? Profile, string Role)>> ListSuggestedUsersAsync(Guid viewerId, int limit, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

