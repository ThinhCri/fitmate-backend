using Fitmate.Application.Common;
using Fitmate.Domain.Identity;

namespace Fitmate.Application.Auth;

/// <summary>
/// Quản lý refresh token: tạo, xoay vòng (rotate), thu hồi (revoke).
/// Token raw chỉ trả cho client 1 lần; DB chỉ lưu SHA-256 hash.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Issue refresh token mới + lưu hash vào DB. Trả raw token cho client.</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, string? deviceLabel, CancellationToken ct);

    /// <summary>
    /// Xoay vòng: validate token raw, revoke token cũ + tạo token mới (rotation).
    /// Trả null nếu token không hợp lệ / hết hạn / đã revoke.
    /// Nếu phát hiện token đã bị revoke trước đó (reuse attack), tự động thu hồi toàn bộ token của user.
    /// </summary>
    Task<IssuedRefreshToken?> RotateAsync(string rawToken, CancellationToken ct);

    /// <summary>Thu hồi token (dùng cho logout). Không throw nếu token không tồn tại.</summary>
    Task RevokeAsync(string rawToken, CancellationToken ct);

    /// <summary>Thu hồi toàn bộ token của một user (dùng khi đổi mật khẩu, reset mật khẩu, xóa tài khoản hoặc phát hiện token reuse).</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}

public record IssuedRefreshToken(string RawToken, DateTime ExpiresAt);

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenStore _store;
    private readonly ITokenHasher _hasher;
    private readonly JwtOptions _options;

    public RefreshTokenService(
        IRefreshTokenStore store,
        ITokenHasher hasher,
        JwtOptions options)
    {
        _store = store;
        _hasher = hasher;
        _options = options;
    }

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, string? deviceLabel, CancellationToken ct)
    {
        var raw = GenerateSecureToken();
        var hash = _hasher.Hash(raw);
        var expires = Clock.UtcNow.AddSeconds(_options.RefreshTokenTtlSeconds);

        var entity = RefreshToken.Create(userId, hash, expires, deviceLabel);
        await _store.AddAsync(entity, ct);
        await _store.SaveChangesAsync(ct);

        return new IssuedRefreshToken(raw, expires);
    }

    public async Task<IssuedRefreshToken?> RotateAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;
        var hash = _hasher.Hash(rawToken);
        var existing = await _store.FindByHashAsync(hash, ct);
        if (existing is null) return null;

        // Replay/Reuse detection: nếu token đã bị revoke trước đó → phát hiện replay attack.
        // Thu hồi toàn bộ refresh tokens đang hoạt động của user này để bảo vệ tài khoản (Family Invalidation).
        if (existing.RevokedAt is not null)
        {
            await _store.RevokeAllForUserAsync(existing.UserId, ct);
            await _store.SaveChangesAsync(ct);
            return null;
        }

        if (Clock.UtcNow >= existing.ExpiresAt) return null;

        // Rotate: revoke token cũ (lưu replacedByHash) + tạo token mới.
        var newRaw = GenerateSecureToken();
        var newHash = _hasher.Hash(newRaw);
        var newExpires = Clock.UtcNow.AddSeconds(_options.RefreshTokenTtlSeconds);

        existing.Revoke(replacedByHash: newHash);
        var next = RefreshToken.Create(existing.UserId, newHash, newExpires, existing.DeviceLabel);
        await _store.AddAsync(next, ct);
        await _store.SaveChangesAsync(ct);

        return new IssuedRefreshToken(newRaw, newExpires);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var hash = _hasher.Hash(rawToken);
        var existing = await _store.FindByHashAsync(hash, ct);
        if (existing is null) return;
        if (existing.RevokedAt is not null) return;
        existing.Revoke();
        await _store.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        await _store.RevokeAllForUserAsync(userId, ct);
        await _store.SaveChangesAsync(ct);
    }

    private static string GenerateSecureToken()
    {
        // 32 bytes random -> base64 url-safe ~ 43 chars. Đủ mạnh cho refresh token.
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
