using Fitmate.Domain.Identity;

namespace Fitmate.Application.Auth;

public record LoginChallenge(string ChallengeId, int ExpiresIn);
public interface IAdminLoginChallengeService
{
    Task<LoginChallenge> CreateAsync(Guid userId, string stamp, CancellationToken ct);
    Task<Guid?> VerifyAsync(string challengeId, string code, bool recoveryCode, CancellationToken ct);
}
public interface IAuthSessionService
{
    Task<AuthSession> CreateAsync(Guid userId, string stamp, bool mfa, CancellationToken ct);
    Task<AuthSession?> FindActiveAsync(Guid sessionId, Guid userId, string stamp, CancellationToken ct);
    Task RevokeAsync(Guid sessionId, Guid userId, CancellationToken ct);
}
