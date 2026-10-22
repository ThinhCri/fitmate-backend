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
    Task<ApplicationUser?> FindIncludingPendingAsync(Guid userId, CancellationToken ct);
    Task<(IReadOnlyList<ApplicationUser> Items, int Total)> ListForAdminAsync(int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<ApplicationUser>> ListDueForFinalizationAsync(DateTime cutoffUtc, int limit, CancellationToken ct);
    Task<T> WithUserLockAsync<T>(Guid userId, Func<Task<T>> action, CancellationToken ct);
}

public interface IDeletionTokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}
