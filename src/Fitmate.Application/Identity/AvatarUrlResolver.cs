using Fitmate.Application.Storage;
using Fitmate.Domain.Identity;

namespace Fitmate.Application.Identity;

/// <summary>
/// Resolves a user's readable avatar URL from their owned storage key when possible,
/// falling back to a legacy direct <see cref="ApplicationUser.AvatarUrl"/> value.
/// </summary>
public static class AvatarUrlResolver
{
    private static readonly TimeSpan ReadUrlTtl = TimeSpan.FromMinutes(15);

    public static string? Resolve(ApplicationUser user, ISignedUrlService? signedUrl)
        => Resolve(user.AvatarStorageKey, user.AvatarUrl, signedUrl);

    public static string? Resolve(string? avatarStorageKey, string? avatarUrl, ISignedUrlService? signedUrl)
    {
        if (!string.IsNullOrWhiteSpace(avatarStorageKey) && signedUrl is not null)
            return signedUrl.CreatePresignedReadUrl(avatarStorageKey, ReadUrlTtl).ToString();
        return avatarUrl;
    }
}
