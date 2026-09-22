using Fitmate.Application.Auth;
using Fitmate.Application.Common;
using Fitmate.Domain.Identity;
using FluentAssertions;

namespace Fitmate.UnitTests;

/// <summary>
/// BE-030: RefreshTokenService create + rotate + revoke với InMemory store.
/// </summary>
public class RefreshTokenServiceTests
{
    [Fact]
    public async Task IssueAsync_StoresHashAndReturnsRaw()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var issued = await svc.IssueAsync(Guid.NewGuid(), "device-A", CancellationToken.None);

        issued.RawToken.Should().NotBeNullOrEmpty();
        store.Added.Should().HaveCount(1);
        store.Added[0].TokenHash.Should().NotBe(issued.RawToken); // hash ≠ raw
        store.Added[0].DeviceLabel.Should().Be("device-A");
    }

    [Fact]
    public async Task RotateAsync_ReturnsNewToken_AndRevokesOld()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();
        var first = await svc.IssueAsync(userId, "d", CancellationToken.None);

        var rotated = await svc.RotateAsync(first.RawToken, CancellationToken.None);
        rotated.Should().NotBeNull();
        rotated!.RawToken.Should().NotBe(first.RawToken);

        // Old token đã revoke.
        var oldEntry = await store.FindByHashAsync(new Sha256TokenHasher().Hash(first.RawToken), CancellationToken.None);
        oldEntry!.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RotateAsync_ReturnsNull_WhenTokenAlreadyRevoked()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var first = await svc.IssueAsync(Guid.NewGuid(), null, CancellationToken.None);
        await svc.RevokeAsync(first.RawToken, CancellationToken.None);

        var rotated = await svc.RotateAsync(first.RawToken, CancellationToken.None);
        rotated.Should().BeNull();
    }

    [Fact]
    public async Task RotateAsync_ReturnsNull_WhenTokenUnknown()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var rotated = await svc.RotateAsync("nonexistent", CancellationToken.None);
        rotated.Should().BeNull();
    }

    [Fact]
    public async Task RevokeAsync_IsIdempotent()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var issued = await svc.IssueAsync(Guid.NewGuid(), null, CancellationToken.None);

        await svc.RevokeAsync(issued.RawToken, CancellationToken.None);
        await svc.RevokeAsync(issued.RawToken, CancellationToken.None); // 2 lần không lỗi
    }

    [Fact]
    public async Task RotateAsync_RevokesAllTokensForUser_WhenTokenReused()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();

        var first = await svc.IssueAsync(userId, "device-1", CancellationToken.None);
        var second = await svc.IssueAsync(userId, "device-2", CancellationToken.None);

        // Rotate first token -> first token is now revoked
        var rotatedFirst = await svc.RotateAsync(first.RawToken, CancellationToken.None);
        rotatedFirst.Should().NotBeNull();

        // Attacker attempts to reuse the already-revoked first token
        var replayAttempt = await svc.RotateAsync(first.RawToken, CancellationToken.None);
        replayAttempt.Should().BeNull();

        // Reuse detection must invalidate all other active tokens for that user
        store.RevokedUserTokens.Should().Contain(userId);
    }

    [Fact]
    public async Task RevokeAllForUserAsync_RevokesAllTokens()
    {
        var store = new InMemoryRefreshTokenStore();
        var svc = NewService(store);
        var userId = Guid.NewGuid();

        await svc.IssueAsync(userId, "d1", CancellationToken.None);
        await svc.RevokeAllForUserAsync(userId, CancellationToken.None);

        store.RevokedUserTokens.Should().Contain(userId);
    }

    private static RefreshTokenService NewService(InMemoryRefreshTokenStore store)
    {
        var opts = new JwtOptions
        {
            SigningKey = new string('a', 64),
            RefreshTokenTtlSeconds = 3600
        };
        return new RefreshTokenService(store, new Sha256TokenHasher(), opts);
    }
}

internal class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    public List<RefreshToken> Added { get; } = new();
    public List<Guid> RevokedUserTokens { get; } = new();
    private readonly Dictionary<string, RefreshToken> _byHash = new();

    public Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        Added.Add(token);
        _byHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
    {
        return Task.FromResult(_byHash.TryGetValue(tokenHash, out var t) ? t : null);
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        RevokedUserTokens.Add(userId);
        foreach (var token in Added.Where(t => t.UserId == userId && t.RevokedAt == null))
        {
            token.Revoke();
        }
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
