using Fitmate.Application.Auth;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;

namespace Fitmate.Application.Identity;

public class AccountDeletionService : IAccountDeletionService
{
    private readonly IAccountDeletionStore _store;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly IOutboxDispatcher _outbox;
    private readonly IDeletionTokenProtector _tokenProtector;

    private const int RetentionDays = 30;

    public AccountDeletionService(
        IAccountDeletionStore store,
        IRefreshTokenStore refreshTokenStore,
        IOutboxDispatcher outbox,
        IDeletionTokenProtector tokenProtector)
    {
        _store = store;
        _refreshTokenStore = refreshTokenStore;
        _outbox = outbox;
        _tokenProtector = tokenProtector;
    }

    public async Task<AccountDeletionStatusDto> RequestDeletionAsync(Guid userId, AccountDeletionRequest request, CancellationToken ct)
    {
        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        if (user.Status == UserStatus.PendingDeletion || user.DeletionRequestedAt.HasValue)
        {
            return BuildStatusDto(user, false);
        }

        var hasActiveBookings = await _store.HasActiveBookingsAsync(userId, ct);
        if (hasActiveBookings)
        {
            throw new ConflictException(ErrorCodes.Identity.HasActiveBookings,
                "Cannot delete account while you have active appointments or pending financial obligations.");
        }

        user.DeletionRequestedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _outbox.Enqueue("user.deletion_requested", new
        {
            UserId = userId,
            RequestedAt = user.DeletionRequestedAt.Value,
            Reason = request.Reason
        });

        await _store.SaveChangesAsync(ct);

        return BuildStatusDto(user, false);
    }

    public async Task<AccountDeletionStatusDto> GetDeletionStatusAsync(Guid userId, CancellationToken ct)
    {
        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        var hasActiveBookings = await _store.HasActiveBookingsAsync(userId, ct);
        return BuildStatusDto(user, hasActiveBookings);
    }

    public async Task<AccountDeletionStatusDto> CancelDeletionAsync(Guid userId, CancellationToken ct)
    {
        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        if (user.Status != UserStatus.Active || !user.DeletionRequestedAt.HasValue)
        {
            throw new ConflictException(ErrorCodes.Identity.DeletionNotPending, "Account is not pending deletion.");
        }

        user.DeletionRequestedAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        _outbox.Enqueue("user.deletion_cancelled", new
        {
            UserId = userId,
            CancelledAt = DateTime.UtcNow
        });

        await _store.SaveChangesAsync(ct);

        return BuildStatusDto(user, false);
    }

    public async Task RequestPublicDeletionAsync(PublicAccountDeletionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return;

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _store.FindUserByEmailAsync(normalizedEmail, ct);

        if (user is null || user.IsDeleted)
        {
            // Return neutrally to avoid email enumeration
            return;
        }

        var expiresAtUtc = DateTime.UtcNow.AddHours(24);
        var payload = $"{user.Id}:{expiresAtUtc.Ticks}:{Guid.NewGuid()}";
        var token = _tokenProtector.Protect(payload);

        _outbox.Enqueue("account.deletion_verification_email", new
        {
            UserId = user.Id,
            Email = user.Email,
            Token = token,
            ExpiresAt = expiresAtUtc,
            Reason = request.Reason
        });

        await _store.SaveChangesAsync(ct);
    }

    public async Task<AccountDeletionStatusDto> ConfirmPublicDeletionAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ValidationException(ErrorCodes.Identity.InvalidToken, "Token must be provided.",
                new Dictionary<string, string[]> { ["token"] = new[] { "Token is required." } });

        Guid userId;
        DateTime expiresAtUtc;

        try
        {
            var unprotected = _tokenProtector.Unprotect(token);
            var parts = unprotected.Split(':');
            if (parts.Length < 3)
                throw new DomainException(ErrorCodes.Identity.InvalidToken, "Invalid deletion token format.");

            userId = Guid.Parse(parts[0]);
            expiresAtUtc = new DateTime(long.Parse(parts[1]), DateTimeKind.Utc);
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            throw new DomainException(ErrorCodes.Identity.InvalidToken, "Invalid or expired deletion token.");
        }

        if (DateTime.UtcNow > expiresAtUtc)
        {
            throw new DomainException(ErrorCodes.Auth.TokenExpired, "Deletion confirmation token has expired.");
        }

        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        if (user.Status == UserStatus.PendingDeletion || user.DeletionRequestedAt.HasValue)
        {
            return BuildStatusDto(user, false);
        }

        var hasActiveBookings = await _store.HasActiveBookingsAsync(userId, ct);
        if (hasActiveBookings)
        {
            throw new ConflictException(ErrorCodes.Identity.HasActiveBookings,
                "Cannot delete account while you have active appointments or pending financial obligations.");
        }

        user.DeletionRequestedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        _outbox.Enqueue("user.deletion_requested", new
        {
            UserId = userId,
            RequestedAt = user.DeletionRequestedAt.Value,
            Source = "public_form"
        });

        await _store.SaveChangesAsync(ct);

        return BuildStatusDto(user, false);
    }

    public async Task<UserDataExportDto> ExportUserDataAsync(Guid userId, CancellationToken ct)
    {
        var user = await _store.FindUserByIdAsync(userId, ct)
            ?? throw new NotFoundException(ErrorCodes.Identity.UserNotFound, $"User {userId} not found.");

        var profile = await _store.GetUserProfileAsync(userId, ct);
        var role = await _store.GetPrimaryRoleAsync(userId, ct) ?? "member";

        UserProfileDto? profileDto = null;
        if (profile is not null)
        {
            profileDto = new UserProfileDto(
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

        return new UserDataExportDto(
            UserId: user.Id,
            Email: user.Email ?? "",
            FullName: user.FullName,
            Timezone: user.Timezone,
            Profile: profileDto,
            ExportedAtUtc: DateTime.UtcNow);
    }

    private static AccountDeletionStatusDto BuildStatusDto(ApplicationUser user, bool hasActiveBookings)
    {
        var isPending = user.DeletionRequestedAt.HasValue || user.Status == UserStatus.PendingDeletion;
        var requestedAt = user.DeletionRequestedAt;
        var scheduledPermanent = user.Status == UserStatus.PendingDeletion ? user.DeletedAt?.AddDays(RetentionDays) : null;
        var daysRemaining = isPending && scheduledPermanent.HasValue
            ? Math.Max(0, (int)Math.Ceiling((scheduledPermanent.Value - DateTime.UtcNow).TotalDays))
            : 0;

        return new AccountDeletionStatusDto(
            UserId: user.Id,
            Status: user.Status.ToString().ToLowerInvariant(),
            IsPendingDeletion: isPending,
            RequestedAt: requestedAt,
            ScheduledPermanentDeletionAt: scheduledPermanent,
            RetentionDaysRemaining: daysRemaining,
            HasActiveBookings: hasActiveBookings);
    }
}
