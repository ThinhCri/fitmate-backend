using Fitmate.Application.Idempotency;
using Fitmate.Domain.Outbox;
using Fitmate.Infrastructure.Persistence;
using Fitmate.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fitmate.IntegrationTests;

public class IdempotencyConcurrencyTests : IClassFixture<FitmateTestAppFactory>
{
    private readonly FitmateTestAppFactory _factory;

    public IdempotencyConcurrencyTests(FitmateTestAppFactory factory)
    {
        _factory = factory;
        _factory.EnsureDatabaseInitialized();
    }

    [Fact]
    public async Task TryAcquireAsync_TenConcurrentRequests_OnlyOneAcquires_NineInProgress()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        var key = $"it-atomic-key-{Guid.NewGuid():N}";
        var payloadHash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
        const int concurrentCount = 10;

        using var barrier = new Barrier(concurrentCount);
        var tasks = Enumerable.Range(0, concurrentCount).Select(async i =>
        {
            await Task.Yield();
            using var taskScope = _factory.Services.CreateScope();
            var taskStore = taskScope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
            var owner = Guid.NewGuid();

            barrier.SignalAndWait();
            return await taskStore.TryAcquireAsync(
                key,
                payloadHash,
                owner,
                leaseDuration: TimeSpan.FromSeconds(30),
                isFinancial: true,
                ttl: null,
                actorUserId: $"user-{i}",
                operationName: "POST /api/v1/payments",
                CancellationToken.None);
        }).ToList();

        var results = await Task.WhenAll(tasks);

        var acquiredCount = results.Count(r => r.Status == IdempotencyAcquireStatus.Acquired);
        var inProgressCount = results.Count(r => r.Status == IdempotencyAcquireStatus.InProgress);

        acquiredCount.Should().Be(1, "Exactly one thread must acquire the atomic lease");
        inProgressCount.Should().Be(9, "All 9 competing threads must receive InProgress status");
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentPayload_ReturnsPayloadConflict()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        var key = $"it-conflict-key-{Guid.NewGuid():N}";
        var hash1 = "1111111111111111111111111111111111111111111111111111111111111111";
        var hash2 = "2222222222222222222222222222222222222222222222222222222222222222";

        var r1 = await store.TryAcquireAsync(
            key, hash1, Guid.NewGuid(), TimeSpan.FromSeconds(30), isFinancial: true, ttl: null, "u1", "POST /test", CancellationToken.None);
        r1.Status.Should().Be(IdempotencyAcquireStatus.Acquired);

        var r2 = await store.TryAcquireAsync(
            key, hash2, Guid.NewGuid(), TimeSpan.FromSeconds(30), isFinancial: true, ttl: null, "u1", "POST /test", CancellationToken.None);
        r2.Status.Should().Be(IdempotencyAcquireStatus.PayloadConflict);
    }

    [Fact]
    public async Task CompleteAsync_WithWrongOwnerToken_FailsToComplete()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

        var key = $"it-owner-key-{Guid.NewGuid():N}";
        var hash = "3333333333333333333333333333333333333333333333333333333333333333";
        var owner = Guid.NewGuid();

        await store.TryAcquireAsync(
            key, hash, owner, TimeSpan.FromSeconds(30), isFinancial: true, ttl: null, "u1", "POST /test", CancellationToken.None);

        var wrongToken = Guid.NewGuid();
        var completedWrong = await store.CompleteAsync(key, wrongToken, 200, "{\"success\":true}", CancellationToken.None);
        completedWrong.Should().BeFalse();

        var completedRight = await store.CompleteAsync(key, owner, 200, "{\"success\":true}", CancellationToken.None);
        completedRight.Should().BeTrue();
    }

    [Fact]
    public async Task PurgeExpiredAsync_PreservesFinancialRecordsForever()
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
        var db = scope.ServiceProvider.GetRequiredService<FitmateDbContext>();

        var finKey = $"it-fin-{Guid.NewGuid():N}";
        var nonFinKey = $"it-nonfin-{Guid.NewGuid():N}";
        var hash = "4444444444444444444444444444444444444444444444444444444444444444";

        var finOwner = Guid.NewGuid();
        var nonFinOwner = Guid.NewGuid();

        await store.TryAcquireAsync(
            finKey, hash, finOwner, TimeSpan.FromSeconds(30), isFinancial: true, ttl: null, "u1", "POST /pay", CancellationToken.None);
        await store.CompleteAsync(finKey, finOwner, 200, "{}", CancellationToken.None);

        await store.TryAcquireAsync(
            nonFinKey, hash, nonFinOwner, TimeSpan.FromSeconds(30), isFinancial: false, ttl: TimeSpan.FromSeconds(1), "u1", "POST /msg", CancellationToken.None);
        await store.CompleteAsync(nonFinKey, nonFinOwner, 200, "{}", CancellationToken.None);

        // Make nonfin expired
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE idempotency_records SET "ExpiresAt" = now() - interval '1 hour' WHERE "Key" = {0}""",
            nonFinKey);

        var purged = await store.PurgeExpiredAsync(DateTime.UtcNow, CancellationToken.None);
        purged.Should().BeGreaterThanOrEqualTo(1);

        var finRecord = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == finKey);
        finRecord.Should().NotBeNull("Financial records must never be purged");

        var nonFinRecord = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Key == nonFinKey);
        nonFinRecord.Should().BeNull("Expired non-financial records must be purged");
    }
}
