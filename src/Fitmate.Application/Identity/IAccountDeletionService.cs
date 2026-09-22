using Fitmate.Domain.Identity;

namespace Fitmate.Application.Identity;

public interface IAccountDeletionService
{
    Task<AccountDeletionStatusDto> RequestDeletionAsync(Guid userId, AccountDeletionRequest request, CancellationToken ct);
    Task<AccountDeletionStatusDto> GetDeletionStatusAsync(Guid userId, CancellationToken ct);
    Task<AccountDeletionStatusDto> CancelDeletionAsync(Guid userId, CancellationToken ct);
    Task RequestPublicDeletionAsync(PublicAccountDeletionRequest request, CancellationToken ct);
    Task<AccountDeletionStatusDto> ConfirmPublicDeletionAsync(string token, CancellationToken ct);
    Task<UserDataExportDto> ExportUserDataAsync(Guid userId, CancellationToken ct);
}

public interface IAccountDeletionStore
{
    Task<ApplicationUser?> FindUserByIdAsync(Guid userId, CancellationToken ct);
    Task<ApplicationUser?> FindUserByEmailAsync(string email, CancellationToken ct);
    Task<bool> HasActiveBookingsAsync(Guid userId, CancellationToken ct);
    Task<UserProfile?> GetUserProfileAsync(Guid userId, CancellationToken ct);
    Task<string?> GetPrimaryRoleAsync(Guid userId, CancellationToken ct);
    Task RevokeUserDevicesAsync(Guid userId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IDeletionTokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}
