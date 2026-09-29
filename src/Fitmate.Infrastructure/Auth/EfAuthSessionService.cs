using System.Security.Cryptography;
using System.Text;
using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Domain.Common;
using Fitmate.Domain.Identity;
using Fitmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Fitmate.Infrastructure.Auth;

public sealed class EfAuthSessionService(FitmateDbContext db, JwtOptions options) : IAuthSessionService
{
    public async Task<AuthSession> CreateAsync(Guid userId, string stamp, bool mfa, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var session = new AuthSession { Id = Guid.NewGuid(), UserId = userId, SecurityStamp = stamp,
            AuthenticationMethod = mfa ? "mfa" : "pwd", MfaVerifiedAt = mfa ? now : null,
            CreatedAt = now, ExpiresAt = now.AddSeconds(options.RefreshTokenTtlSeconds) };
        db.Set<AuthSession>().Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }
    public Task<AuthSession?> FindActiveAsync(Guid sessionId, Guid userId, string stamp, CancellationToken ct) =>
        db.Set<AuthSession>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId &&
            x.SecurityStamp == stamp && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow, ct);
    public async Task RevokeAsync(Guid sessionId, Guid userId, CancellationToken ct)
    {
        var session = await db.Set<AuthSession>().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, ct);
        if (session is null) return;
        session.RevokedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

public sealed class EfAdminLoginChallengeService(FitmateDbContext db, UserManager<ApplicationUser> users) : IAdminLoginChallengeService
{
    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    public async Task<LoginChallenge> CreateAsync(Guid userId, string stamp, CancellationToken ct)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.Set<AdminLoginChallenge>().Add(new() { TokenHash = Hash(raw), UserId = userId,
            SecurityStamp = stamp, ExpiresAt = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync(ct);
        return new(raw, 300);
    }
    public async Task<Guid?> VerifyAsync(string challengeId, string code, bool recoveryCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || challengeId.Length > 128 || string.IsNullOrWhiteSpace(code) || code.Length > 100) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = Hash(challengeId);
        // Serialize factor consumption across instances, including one-use recovery codes.
        var challenge = await db.Set<AdminLoginChallenge>().FromSqlInterpolated(
            $"SELECT * FROM admin_login_challenges WHERE \"TokenHash\" = {hash} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (challenge is null || challenge.Consumed || challenge.Attempts >= 5 || challenge.ExpiresAt <= DateTime.UtcNow) return null;
        challenge.Attempts++;
        var user = await users.FindByIdAsync(challenge.UserId.ToString());
        var valid = user is not null && !user.IsDeleted && user.Status == UserStatus.Active && user.TwoFactorEnabled &&
            user.SecurityStamp == challenge.SecurityStamp && !await users.IsLockedOutAsync(user) &&
            (await users.GetRolesAsync(user)).Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase));
        if (valid)
        {
            valid = recoveryCode
                ? (await users.RedeemTwoFactorRecoveryCodeAsync(user!, code.Trim())).Succeeded
                : await users.VerifyTwoFactorTokenAsync(user!, users.Options.Tokens.AuthenticatorTokenProvider, code.Trim());
        }
        if (valid) { challenge.Consumed = true; await users.ResetAccessFailedCountAsync(user!); }
        else if (user is not null) await users.AccessFailedAsync(user);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return valid ? challenge.UserId : null;
    }
}
