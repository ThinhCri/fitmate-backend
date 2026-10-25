using System.Security.Cryptography;
using System.Text.Json;
using Fitmate.Application.Identity;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Auth;

public sealed class IdentityAccountDeletionOtpGateway(
    FitmateDbContext db, IOutboxDispatcher outbox, IDeletionTokenProtector protector) : IAccountDeletionOtpGateway
{
    private const string Provider = "fitmate-account-deletion-otp-v1";
    private const string TokenName = "challenge";
    private sealed record Challenge(string Email, string ProtectedCode, DateTime ExpiresAt, DateTime SentAt, int Attempts);

    public Task<DeletionOtpStartResult> StartAsync(Guid userId, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var user = await LockUserAsync(userId, ct);
            var row = await FindChallengeAsync(userId, ct);
            var previous = Read(row);
            var now = DateTime.UtcNow;
            if (previous is not null && previous.SentAt.AddSeconds(60) > now)
                throw new DomainException("DELETION_OTP_RESEND_TOO_SOON", "Vui lòng chờ 60 giây trước khi gửi lại mã.", 429);
            if (user.DeletionRequestedAt.HasValue)
                throw new ConflictException("DELETION_ALREADY_REQUESTED", "Tài khoản đã có yêu cầu xóa đang chờ xử lý.");
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            var challenge = new Challenge(user.Email!, protector.Protect(code), now.AddMinutes(5), now, 0);
            if (row is null)
            {
                row = new IdentityUserToken<Guid> { UserId = userId, LoginProvider = Provider, Name = TokenName };
                db.UserTokens.Add(row);
            }
            row.Value = JsonSerializer.Serialize(challenge);
            outbox.Enqueue("account.deletion_otp_email", new DeletionOtpEmailPayload(
                protector.Protect(user.Email!), challenge.ProtectedCode, challenge.ExpiresAt));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new DeletionOtpStartResult(challenge.ExpiresAt, 60);
        });

    public async Task<AccountDeletionStatusDto> ExecuteVerifiedAsync(Guid userId, string code,
        Func<Task<AccountDeletionStatusDto>> action, CancellationToken ct)
    {
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var user = await LockUserAsync(userId, ct);
            var row = await FindChallengeAsync(userId, ct);
            var challenge = Read(row);
            if (challenge is null || challenge.ExpiresAt <= DateTime.UtcNow || challenge.Attempts >= 5 ||
                !string.Equals(challenge.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(code),
                    System.Text.Encoding.UTF8.GetBytes(protector.Unprotect(challenge.ProtectedCode))))
            {
                row!.Value = JsonSerializer.Serialize(challenge with { Attempts = challenge.Attempts + 1 });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return null;
            }
            // Consume and mutate atomically, under the same user lock.
            db.UserTokens.Remove(row!);
            var status = await action();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return status;
        });
        return result ?? throw new DomainException("DELETION_OTP_INVALID", "Mã OTP không đúng, đã hết hạn hoặc đã sử dụng. Vui lòng gửi lại mã nếu cần.", 400);
    }

    private async Task<ApplicationUser> LockUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM asp_net_users WHERE \"Id\" = {userId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || string.IsNullOrWhiteSpace(user.Email))
            throw new ForbiddenException("DELETION_OTP_FORBIDDEN", "Tài khoản không thể yêu cầu xác thực xóa.");
        return user;
    }

    private Task<IdentityUserToken<Guid>?> FindChallengeAsync(Guid userId, CancellationToken ct) =>
        db.UserTokens.SingleOrDefaultAsync(t => t.UserId == userId && t.LoginProvider == Provider && t.Name == TokenName, ct);
    private static Challenge? Read(IdentityUserToken<Guid>? row) => row?.Value is null ? null : JsonSerializer.Deserialize<Challenge>(row.Value);
}
