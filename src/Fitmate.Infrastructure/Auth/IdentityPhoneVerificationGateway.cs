using System.Text.Json;
using Fitmate.Application.Auth;
using Fitmate.Application.Outbox;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Auth;

/// <summary>Stores a single active challenge per PT in Identity's durable user-token table.</summary>
public sealed class IdentityPhoneVerificationGateway(
    FitmateDbContext db,
    UserManager<ApplicationUser> users,
    IOutboxDispatcher outbox,
    IPhoneOtpProtector protector) : IPhoneVerificationGateway
{
    private const string Provider = "fitmate-phone-verification-v1";
    private const string TokenName = "challenge";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private sealed record Challenge(string Phone, DateTime ExpiresAtUtc, DateTime SentAtUtc, int Attempts);

    public Task<PhoneVerificationStartResult> StartAsync(Guid userId, string phoneNumberE164, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => StartInTransactionAsync(userId, phoneNumberE164, ct));

    private async Task<PhoneVerificationStartResult> StartInTransactionAsync(Guid userId, string phoneNumberE164, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(userId, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || !user.EmailConfirmed)
            throw new ForbiddenException("PHONE_VERIFICATION_FORBIDDEN", "Only an active, email-verified PT can verify a phone.");
        if (user.PhoneNumberConfirmed && user.PhoneNumber == phoneNumberE164)
            throw new ConflictException("PHONE_ALREADY_VERIFIED", "This phone number is already verified.");
        if (await db.Users.AnyAsync(u => u.Id != userId && u.PhoneNumberConfirmed && u.PhoneNumber == phoneNumberE164, ct))
            throw new ConflictException("PHONE_ALREADY_USED", "This phone number is already in use.");

        var row = await db.UserTokens.SingleOrDefaultAsync(t => t.UserId == userId &&
            t.LoginProvider == Provider && t.Name == TokenName, ct);
        if (row?.Value is { } value)
        {
            var existing = JsonSerializer.Deserialize<Challenge>(value);
            if (existing is not null && existing.SentAtUtc.Add(ResendCooldown) > DateTime.UtcNow)
                throw new DomainException("PHONE_RESEND_TOO_SOON", "Vui lòng chờ trước khi gửi lại OTP.", 429);
        }

        var code = await users.GenerateChangePhoneNumberTokenAsync(user, phoneNumberE164);
        var now = DateTime.UtcNow;
        var challenge = new Challenge(phoneNumberE164, now.Add(Ttl), now, 0);
        if (row is null)
        {
            row = new IdentityUserToken<Guid> { UserId = userId, LoginProvider = Provider, Name = TokenName };
            db.UserTokens.Add(row);
        }
        row.Value = JsonSerializer.Serialize(challenge);
        outbox.Enqueue("auth.phone_verification", new PhoneOtpOutboxPayload(userId,
            protector.Protect(phoneNumberE164), protector.Protect(code), challenge.ExpiresAtUtc));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new PhoneVerificationStartResult(challenge.ExpiresAtUtc, (int)ResendCooldown.TotalSeconds);
    }

    public Task<bool> ConfirmAsync(Guid userId, string code, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => ConfirmInTransactionAsync(userId, code, ct));

    private async Task<bool> ConfirmInTransactionAsync(Guid userId, string code, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUserAsync(userId, ct);
        if (user is null || user.IsDeleted || user.Status != UserStatus.Active || !user.EmailConfirmed)
            throw new ForbiddenException("PHONE_VERIFICATION_FORBIDDEN", "Only an active, email-verified PT can verify a phone.");
        var row = await db.UserTokens.SingleOrDefaultAsync(t => t.UserId == userId &&
            t.LoginProvider == Provider && t.Name == TokenName, ct);
        var challenge = row?.Value is null ? null : JsonSerializer.Deserialize<Challenge>(row.Value);
        if (challenge is null || challenge.ExpiresAtUtc <= DateTime.UtcNow || challenge.Attempts >= 5)
            return false;

        // Failed attempts are committed so retries cannot reset the counter.
        if (!await users.VerifyChangePhoneNumberTokenAsync(user, code, challenge.Phone))
        {
            row!.Value = JsonSerializer.Serialize(challenge with { Attempts = challenge.Attempts + 1 });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return false;
        }
        if (await db.Users.AnyAsync(u => u.Id != userId && u.PhoneNumberConfirmed && u.PhoneNumber == challenge.Phone, ct))
            throw new ConflictException("PHONE_ALREADY_USED", "This phone number is already in use.");
        var result = await users.ChangePhoneNumberAsync(user, challenge.Phone, code);
        if (!result.Succeeded)
            throw new ConflictException("PHONE_CODE_INVALID", "Mã xác minh không còn hợp lệ.");
        db.UserTokens.Remove(row!);
        db.AuditLogs.Add(AuditLog.Record(userId, "PT", "pt.phone_verified", "User", userId));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private Task<ApplicationUser?> LockUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.FromSqlInterpolated($"SELECT * FROM asp_net_users WHERE \"Id\" = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
}
